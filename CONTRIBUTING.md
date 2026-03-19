# Contributing to ArchLens <!-- omit in toc -->

Thank you for your interest in contributing! This document covers how to set up a development environment, build the project, and work on the VSCode extension.

## Table of Contents <!-- omit in toc -->

- [Prerequisites](#prerequisites)
- [Setting Up the Development Environment](#setting-up-the-development-environment)
- [Building and Publishing the .NET Component](#building-and-publishing-the-net-component)
- [Running ArchLens Locally](#running-archlens-locally)
- [Debugging C#](#debugging-c)
- [Architectural Requirements](#architectural-requirements)
- [Example extending ArchLens to parse a new language](#example-extending-archlens-to-parse-a-new-language)
- [Running Tests](#running-tests)
  - [Python tests](#python-tests)
  - [C# / .NET tests](#c--net-tests)

## Prerequisites

- **Python 3.10 or 3.12** (do not use 3.14; 3.12 and 3.10 are known to work)
- **.NET 8 SDK**
- **VS Code** with the Python extension installed

## Setting Up the Development Environment

1. **Clone the ArchLens repository:**

    ```bash
    git clone https://github.com/archlens/ArchLens
    cd ArchLens
    ```

2. **Clone the target project** you want to test against (or use ArchLens itself).

3. **Create and activate a virtual environment** in your target project:

    ```bash
    python -m venv .venv
    # Windows
    .venv\Scripts\activate
    # macOS/Linux
    source .venv/bin/activate
    ```

4. **Install ArchLens in editable mode** from the local source:

    ```bash
    pip install -e "<path-to-archlens>/src/python"
    ```

    This means any changes you make to the Python source are immediately reflected without reinstalling.

5. **Install Python dev dependencies:**

    ```bash
    python -m pip install -r src/python/requirements.txt
    ```

## Building and Publishing the .NET Component

The ArchLens CLI invokes a .NET DLL for parsing and analysis. After making changes to the C# source, you must republish the project to make the updated DLL available to the Python CLI:

```bash
dotnet publish "<path-to-archlens>/src/c-sharp/Archlens.csproj" \
    -o "<path-to-archlens>/src/python/src/.dotnet"
```

This places the compiled output (including the DLL) in the `.dotnet` folder, which is where the Python CLI looks for it.

> **Important:** The `.csproj` file must **not** include `<OutputType>Exe</OutputType>` when building for the CLI. That setting is only used for debugging (see [Debugging C#](#debugging-c)).

## Running ArchLens Locally

With the virtual environment active and the .NET component built, you can run ArchLens CLI commands directly using the Python entry point:

```bash
python ./src/python/src/cli_interface.py [command]
```

Available commands: `init`, `render`, `render-diff`, `create-action`.

Alternatively, since the package is installed in editable mode, the `archlens` command should be available directly:

```bash
archlens render
```

## Debugging C#

To run the C# project as a standalone executable for debugging purposes, temporarily add the following to `Archlens.csproj`:

```xml
<OutputType>Exe</OutputType>
```

This allows you to run and step through the C# code directly. Remember to **remove this line** before building for use with the Python CLI.

## Architectural Requirements

ArchLens is build after Clean- / Onion architecture.

- The Domain layer encapsulates the business-critical concepts and rules, containing the core models, interfaces and logic for building dependency graphs, as well as caching mechanisms.
- The Application layer coordinates the domain logic, namely, detecting changes in the analysed project, constructing the dependency graph, and rendering output.
- The Infra layer provides concrete implementations of the domain interfaces, such as parsers, file system and Git access, configuration loading and serialisation (**This is the most important layer if you want to extend the tool to parse a new language or render in a new format**).
- Finally, the CLI layer acts as an entry point, and structures the injected services.

Dependencies point inward: the CLI may depend on Infra, Application, and Domain, Infra on Application and Domain, Application solely on Domain, and Domain has no dependencies on outer layers.

## Example extending ArchLens to parse a new language

The ArchLens C# project is designed to be easily extended to parse other languages. When adding support for a new language, you change the following four files:

1. In the `Domain/Models/Enums/Language.cs` enum file add an enum for the language you want to be able to parse.
2. In `Infra/Parser/` add a file for the parser behaviour you want. Let the class inherit from `IDependencyParser` and implement the `ParseFileDependencies` method to parse file dependencies.
3. In the `SelectDependencyParser` method in `Infra/Factories/DependencyParserFactory.cs`, add a switch case that delegates your parser if it matches the new language enum.
4. In the `MapLanguage` method in `Infra/ConfigManager.cs` add a switch case that maps the configuration language name to the language enum.

## Running Tests

### Python tests

With the virtual environment active:

```bash
python -m pytest
```

### C# / .NET tests

```bash
dotnet test src/c-sharp/Archlens.sln
```
