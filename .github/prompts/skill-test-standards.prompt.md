---
agent: agent
description: Generate a complete xUnit test plan for a C# method from a behavior description and verify assumptions with Falsify Claims.
---

#file:.github/prompts/skill-falsify-claims.prompt.md

# Skill: Test Standards

## Purpose
Given a C# method signature and a plain-English behavior description, produce a complete named list of xUnit [Fact]
and [Theory] test cases. Every assumption embedded in the plan must be verified by Falsify Claims before final output.

## Input (supplied by caller)

```
Method:      <C# method signature>
Description: <Plain-English functional description>
```

## Procedure

### Step 1 - Identify test categories

For the given method, enumerate tests across all applicable categories:

| Category | Examples |
|---|---|
| Happy path | Typical inputs produce expected output |
| Identity elements | Adding zero, multiplying by one |
| Algebraic rules | Commutativity, associativity where applicable |
| Boundary values | Minimum, maximum, empty, null, default |
| Error behavior | Invalid input or illegal state throws expected exception |
| Parse/format round-trip | Parse(x.ToString()) equals x |
| Large input correctness | Values beyond small/native ranges |
| Concurrency behavior | Thread-safety guarantees if method is shared-state sensitive |

Only include categories relevant to the method semantics.

### Step 2 - Name each test

Use the convention: MethodName_GivenScenario_ExpectedResult

### Step 3 - State the assumption for each test

For each named test, write one sentence describing the assumption it encodes.

### Step 4 - Run Falsify Claims on all assumptions

Collect every assumption from Step 3 into a numbered list and invoke Falsify Claims.
If any assumption is Falsified, revise affected tests and repeat until zero Falsified rows remain.

## Output Format

After all assumptions are Supported, output:

```
### Test Plan for <MethodName>

1. MethodName_GivenScenario_ExpectedResult
   *Assumption*: <one sentence>

2. MethodName_GivenScenario_ExpectedResult
   *Assumption*: <one sentence>
```

Then state: All assumptions Supported. Test plan is ready.
