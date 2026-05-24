using Modus.Core.Events;
using Modus.Core.Messaging;

namespace Modus.Core.Plugins;

public static class PluginContractValidator
{
    public static ContractValidationResult Validate<T>(T candidate) where T : class
    {
        return Validate(candidate, new PluginContractValidationPolicy());
    }

    public static ContractValidationResult Validate<T>(T candidate, PluginContractValidationPolicy policy) where T : class
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var missing = new List<CapabilityName>();
        var registeredMissing = new HashSet<string>(StringComparer.Ordinal);

        void AddMissing(string capability)
        {
            if (registeredMissing.Add(capability))
            {
                missing.Add(new CapabilityName(capability));
            }
        }

        if (candidate is not IPluginContract)
        {
            AddMissing(nameof(IPluginContract));
        }
        else if (candidate is IPluginContract pluginContract)
        {
            if (string.IsNullOrWhiteSpace(pluginContract.PluginId.Value))
            {
                AddMissing("IPluginContract.PluginId");
            }

            if (string.IsNullOrWhiteSpace(pluginContract.ContractName.Value))
            {
                AddMissing("IPluginContract.ContractName");
            }

            if (pluginContract.ContractVersion is null)
            {
                AddMissing("IPluginContract.ContractVersion");
            }
        }

        if (candidate is not IPluginLifecycle)
        {
            AddMissing(nameof(IPluginLifecycle));
        }


        if (candidate is not IEventSubscriber)
        {
            AddMissing(nameof(IEventSubscriber));
        }

        if (!HasSupportedSyncResponderContract(candidate))
        {
            AddMissing(nameof(ISyncResponder));
        }

        if (candidate is not IPluginOperationCatalog)
        {
            AddMissing(nameof(IPluginOperationCatalog));
        }
        else if (candidate is IPluginOperationCatalog operationCatalog)
        {
            var supportedOperations = operationCatalog.SupportedOperations;
            if (supportedOperations is null)
            {
                AddMissing("IPluginOperationCatalog.SupportedOperations");
            }
            else if (supportedOperations.Count == 0)
            {
                AddMissing("IPluginOperationCatalog.SupportedOperations");
            }
            else
            {
                var normalizedOperations = supportedOperations
                    .Select(operation => operation.Value.Trim())
                    .ToArray();

                if (normalizedOperations.Any(string.IsNullOrWhiteSpace))
                {
                    AddMissing("IPluginOperationCatalog.SupportedOperations");
                }
                else
                {
                    var deterministicOperations = normalizedOperations
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(x => x, StringComparer.Ordinal)
                        .ToArray();

                    if (deterministicOperations.Length != normalizedOperations.Length
                        || !normalizedOperations.SequenceEqual(deterministicOperations, StringComparer.Ordinal))
                    {
                        AddMissing("IPluginOperationCatalog.SupportedOperations");
                    }
                }
            }
        }

        if (policy.RequireScheduledEventsCapability && candidate is not IPluginScheduledEvents)
        {
            AddMissing(nameof(IPluginScheduledEvents));
        }

        if (policy.RequireDeterministicRegistrationLifecycle)
        {
            if (candidate is not IPluginRegistrationPolicy registrationPolicy || candidate is not IPluginContract pluginContract)
            {
                AddMissing(nameof(IPluginRegistrationPolicy));
            }
            else
            {
                var firstPlan = registrationPolicy.BuildRegistrationPlan(pluginContract);
                var secondPlan = registrationPolicy.BuildRegistrationPlan(pluginContract);

                if (firstPlan is null || secondPlan is null || firstPlan.Count == 0 || secondPlan.Count == 0)
                {
                    AddMissing("IPluginRegistrationPolicy.BuildRegistrationPlan");
                }
                else if (!firstPlan.SequenceEqual(secondPlan))
                {
                    AddMissing("IPluginRegistrationPolicy.DeterministicOrdering");
                }
                else
                {
                    for (var index = 0; index < firstPlan.Count; index++)
                    {
                        var step = firstPlan[index];
                        if (step.Sequence != index + 1 || string.IsNullOrWhiteSpace(step.Name))
                        {
                            AddMissing("IPluginRegistrationPolicy.BuildRegistrationPlan");
                            break;
                        }
                    }
                }
            }
        }

        return new ContractValidationResult(IsValid: missing.Count == 0, MissingCapabilities: missing);
    }

    private static bool HasSupportedSyncResponderContract(object candidate)
    {
        if (candidate is ISyncResponder)
        {
            return true;
        }

        return candidate
            .GetType()
            .GetInterfaces()
            .Any(static interfaceType =>
                interfaceType.IsGenericType
                && interfaceType.GetGenericTypeDefinition() == typeof(ISyncResponder<,>)
                && interfaceType.GenericTypeArguments[0] == typeof(SyncRequest)
                && interfaceType.GenericTypeArguments[1].IsGenericType
                && interfaceType.GenericTypeArguments[1].GetGenericTypeDefinition() == typeof(SyncResponse<>));
    }
}