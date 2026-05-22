param(
    [Parameter(Mandatory = $true)]
    [string]$ApiBaseUrl,

    [Parameter(Mandatory = $true)]
    [string]$PackagePath,

    [Parameter(Mandatory = $true)]
    [string]$SignaturePath,

    [int]$PollIntervalMilliseconds = 1000,
    [int]$MaxPollAttempts = 120,
    [string]$BearerToken
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Net.Http

function Fail([string]$Message) {
    Write-Error $Message
    exit 1
}

function Parse-JsonObject([string]$JsonText, [string]$FailureContext) {
    try {
        return $JsonText | ConvertFrom-Json
    }
    catch {
        Fail "$FailureContext Response body was not valid JSON."
    }
}

if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf)) {
    Fail "Package file was not found at path '$PackagePath'."
}

if (-not (Test-Path -LiteralPath $SignaturePath -PathType Leaf)) {
    Fail "Signature file was not found at path '$SignaturePath'."
}

if ($PollIntervalMilliseconds -lt 10) {
    Fail "PollIntervalMilliseconds must be at least 10."
}

if ($MaxPollAttempts -lt 1) {
    Fail "MaxPollAttempts must be greater than or equal to 1."
}

$resolvedPackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
$resolvedSignaturePath = (Resolve-Path -LiteralPath $SignaturePath).Path
$normalizedBaseUrl = $ApiBaseUrl.TrimEnd('/')

if ([string]::IsNullOrWhiteSpace($normalizedBaseUrl)) {
    Fail "ApiBaseUrl must not be empty."
}

$uploadUrl = "$normalizedBaseUrl/management/plugins/uploads"
$httpClient = [System.Net.Http.HttpClient]::new()

try {
    if (-not [string]::IsNullOrWhiteSpace($BearerToken)) {
        $httpClient.DefaultRequestHeaders.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $BearerToken)
    }

    $content = [System.Net.Http.MultipartFormDataContent]::new()
    try {
        $packageBytes = [IO.File]::ReadAllBytes($resolvedPackagePath)
        $signatureBytes = [IO.File]::ReadAllBytes($resolvedSignaturePath)

        $packageContent = [System.Net.Http.ByteArrayContent]::new($packageBytes)
        $signatureContent = [System.Net.Http.ByteArrayContent]::new($signatureBytes)
        $packageContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse("application/zip")
        $signatureContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse("application/octet-stream")

        $content.Add($packageContent, "package", [IO.Path]::GetFileName($resolvedPackagePath))
        $content.Add($signatureContent, "signature", [IO.Path]::GetFileName($resolvedSignaturePath))

        $response = $httpClient.PostAsync($uploadUrl, $content).GetAwaiter().GetResult()
        try {
            $responseBody = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()

            if ($response.StatusCode -eq [System.Net.HttpStatusCode]::Unauthorized -or
                $response.StatusCode -eq [System.Net.HttpStatusCode]::Forbidden) {
                Fail "Upload request failed authorization with status '$($response.StatusCode)'. Check API credentials or operator authorization policy."
            }

            if ($response.StatusCode -ne [System.Net.HttpStatusCode]::Accepted) {
                Fail "Upload request returned unexpected status '$($response.StatusCode)'. Body: $responseBody"
            }

            $acceptedPayload = Parse-JsonObject $responseBody "Upload accepted response parse failure."
            $operationId = [string]$acceptedPayload.operationId
            if ([string]::IsNullOrWhiteSpace($operationId)) {
                Fail "Upload response did not include operationId."
            }

            Write-Output "OperationId=$operationId"

            $statusUrl = "$normalizedBaseUrl/management/plugins/uploads/$operationId"

            for ($attempt = 1; $attempt -le $MaxPollAttempts; $attempt++) {
                $statusResponse = $httpClient.GetAsync($statusUrl).GetAwaiter().GetResult()
                try {
                    $statusBody = $statusResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()

                    if ($statusResponse.StatusCode -eq [System.Net.HttpStatusCode]::Unauthorized -or
                        $statusResponse.StatusCode -eq [System.Net.HttpStatusCode]::Forbidden) {
                        Fail "Polling request failed authorization with status '$($statusResponse.StatusCode)'. Check API credentials or operator authorization policy."
                    }

                    if (-not $statusResponse.IsSuccessStatusCode) {
                        Fail "Polling request returned status '$($statusResponse.StatusCode)'. Body: $statusBody"
                    }

                    $status = Parse-JsonObject $statusBody "Polling response parse failure."
                    $stage = [string]$status.stage
                    $progress = [int]$status.progressPercent
                    $isTerminal = [bool]$status.isTerminal

                    Write-Output "PollAttempt=$attempt Stage=$stage ProgressPercent=$progress"

                    if ($isTerminal) {
                        $isSuccess = [bool]$status.isSuccess
                        Write-Output "FinalStage=$stage"
                        Write-Output "FinalIsSuccess=$isSuccess"

                        if ($isSuccess) {
                            exit 0
                        }

                        $failureReason = [string]$status.failureReason
                        if ([string]::IsNullOrWhiteSpace($failureReason)) {
                            $failureReason = "Upload operation reached failed terminal state without a failure reason."
                        }

                        Fail "Upload operation failed. Reason: $failureReason"
                    }
                }
                finally {
                    $statusResponse.Dispose()
                }

                Start-Sleep -Milliseconds $PollIntervalMilliseconds
            }

            Fail "Upload operation did not reach terminal state after $MaxPollAttempts polling attempts."
        }
        finally {
            $response.Dispose()
        }
    }
    finally {
        $content.Dispose()
    }
}
finally {
    $httpClient.Dispose()
}
