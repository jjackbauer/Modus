---
agent: agent
description: Audit a source implementation against its C# counterpart and produce a completeness checklist plus a Mermaid class diagram.
---

#file:.github/prompts/skill-falsify-claims.prompt.md

# Skill: Implementation Completeness

## Purpose
Given a source class and a target C# project, map public members to C# equivalents,
flag stubs or missing implementations, and produce a completeness checklist with a Mermaid class diagram.

## Input (supplied by caller)

```
SourceClass:          <Source class name>
SourcePath:           <Path to source implementation root, for example ../reference-source/>
SourceFileExtensions: <Comma-separated list, for example .hpp,.cpp or .java or .ts>
SourceLanguage:       <Source language name>
TargetLanguage:       <Target language name, defaults to C#>
CsProject:            <Target C# project path or name>
```

## Procedure

### Step 1 - Read source implementation

Read source files for SourceClass under SourcePath using SourceFileExtensions.
List every public method, operator, and contract-relevant member.

### Step 2 - Read C# counterpart

Read all .cs files under CsProject.
Note which members exist, and which are stubs (empty body, explicit TODO, or throw new NotImplementedException).

### Step 3 - Build mapping table

For each source public member:

| Source Member | C# Equivalent | Interface/Contract | Status |
|---|---|---|---|
| <source member> | <C# member> | <interface or contract> | Missing / Stub / Implemented |

Use status symbols:
- Implemented
- Stub
- Missing

### Step 4 - Run Falsify Claims on each mapping

Collect every mapping claim and invoke Falsify Claims.
Revise any Falsified mappings and repeat until zero Falsified rows remain.

### Step 5 - Produce Mermaid class diagram

Generate a Mermaid classDiagram showing:
- Target class and related types
- Inheritance and interface implementation
- Composition/dependency links relevant to the mapping

### Step 6 - Produce checklist

Output an unchecked checklist of missing or stub members in dependency order.

## Output

1. Mapping table (after Falsify Claims passes)
2. Mermaid class diagram
3. Completeness checklist (unchecked items only, ordered by dependency)
