# ArchLens User Guide  <!-- omit in toc -->

ArchLens generates customisable visual internal dependency diagrams of your codebase, showing packages and their dependencies. It currently supports Python, C#, Go, Java & Kotlin. ArchLens also have a dedicted Visual Studio Code extension, you can call it through CLI and can highlight the differences between GitHub branches to make pull request reviews easier.

## Table of Contents  <!-- omit in toc -->

- [Installation](#installation)
  - [Python projects (PyPi)](#python-projects-pypi)
  - [C# projects and multi-language support](#c-projects-and-multi-language-support)
- [Configuration](#configuration)
  - [Configuration reference](#configuration-reference)
    - [Python folder depth constraint](#python-folder-depth-constraint)
    - [C# configuration example](#c-configuration-example)
- [Commands](#commands)
- [Defining Views](#defining-views)
  - [Show all top-level packages](#show-all-top-level-packages)
  - [Expand a specific package](#expand-a-specific-package)
  - [Multiple packages in one view](#multiple-packages-in-one-view)
  - [Filtering packages](#filtering-packages)
- [Diff Views](#diff-views)
- [VSCode Extension](#vscode-extension)
  - [Installation](#installation-1)
  - [Setup](#setup)
  - [Views in the extension](#views-in-the-extension)
  - [Using the local ArchLens build with the extension](#using-the-local-archlens-build-with-the-extension)
- [Multi-Language Support and Better Performance](#multi-language-support-and-better-performance)
  - [Setup steps](#setup-steps)

## Installation

### Python projects (PyPi)

For Python projects, install ArchLens from PyPi:

```bash
pip install archlens
```

> **Note:** Administrative rights may be required. We recommend using a virtual environment.
>
> **Python version:** ArchLens has been tested on Python 3.9–3.11. Do **not** use Python 3.14. Python 3.12 and 3.10 are recommended.

### C# projects and multi-language support

The PyPi package currently supports Python only. For C# projects, or for the latest features and performance improvements, use the local development version. See [Multi-Language Support and Better Performance](#multi-language-support-and-better-performance).

## Configuration

ArchLens is configured through an `archlens.json` file in the root of your project. To generate a template, either press `ctrl`+`shift`+`p` and select `ArchLens: setup ArchLens`, or run:

```bash
archlens init
```

This creates an `archlens.json` file with the following structure:

```json
{
    "$schema": "https://raw.githubusercontent.com/archlens/ArchLens/master/src/config.schema.json",
    "name": "my-project",
    "rootFolder": "src",
    "github": {
        "url": "https://github.com/owner/my-project",
        "branch": "main"
    },
    "saveLocation": "./diagrams/",
    "views": {
        "completeView": {
            "packages": [],
            "ignorePackages": []
        }
    }
}
```

### Configuration reference

| Field | Required | Description |
|---|---|---|
| `name` | Yes | Name of your project |
| `rootFolder` | Yes | Path to your source root relative to the project root (e.g. `"src"`) |
| `github.url` | For diff commands | URL of the GitHub repository |
| `github.branch` | For diff commands | The base branch to compare against (e.g. `"main"`) |
| `fileExtensions` | No/Required for non-python projects | File extensions to parse (e.g. `[".cs"]`) |
| `exclusions` | No | Folders or files to exclude (e.g. `["obj/", "bin/", "*test*"]`) |
| `saveLocation` | No | Where to save generated diagrams. Defaults to `"./diagrams/"` |
| `snapshotDir` | No | Directory for cache files. Defaults to `".archlens"` |
| `snapshotFile` | No | Filename for the cache. Defaults to `"snapshot"` |

#### Python folder depth constraint

For Python projects, `rootFolder` must have only **one level of depth** from your project root for dependency arrows to render correctly. For example, use `"src"` rather than `"src/myapp/modules"`.

#### C# configuration example

```json
{
    "$schema": "https://raw.githubusercontent.com/archlens/ArchLens/master/src/config.schema.json",
    "name": "MyApp",
    "rootFolder": "src/MyApp",
    "github": {
        "url": "https://github.com/owner/myapp",
        "branch": "main"
    },
    "fileExtensions": [".cs"],
    "exclusions": ["obj/", "bin/", ".vs/", ".git/"],
    "saveLocation": "./diagrams/",
    "views": {
        "completeView": {
            "packages": [],
            "ignorePackages": []
        }
    }
}
```

## Commands

All commands must be run from the **root of your project** (where `archlens.json` lives).

| Command | Description |
|---|---|
| `archlens init` | Creates the `archlens.json` config template |
| `archlens render` | Renders all views defined in the config |
| `archlens render-diff` | Renders difference views comparing current branch to the base branch |
| `archlens create-action` | Creates a GitHub Actions workflow for automatic PR diff comments |

## Defining Views

Views control what is shown in each diagram. Each view is a named entry under `"views"` in your config.

### Show all top-level packages

Leave `packages` empty to show all top-level packages:

```json
"views": {
    "completeView": {
        "packages": [],
        "ignorePackages": []
    }
}
```

### Expand a specific package

Use a `path` and `depth` to drill into a package. `depth: 1` shows the direct children:

```json
"inside-core": {
    "packages": [
        {
            "path": "core",
            "depth": 1
        }
    ]
}
```

### Multiple packages in one view

```json
"layeredView": {
    "packages": [
        { "path": "Application", "depth": 1 },
        { "path": "Domain", "depth": 2 },
        { "path": "Infra", "depth": 1 }
    ],
    "ignorePackages": []
}
```

### Filtering packages

Use `ignorePackages` to remove noisy packages from a view. Two filtering modes are supported:

```json
"ignorePackages": [
    "*test*",
    "core/model"
]
```

- `"*test*"` — removes any package whose name contains `"test"`
- `"core/model"` — removes `core/model` and all of its sub-packages

## Diff Views

Diff views highlight dependency changes between your current branch and the base branch specified in `github.branch`. Changed elements are shown in **green** (added) and **red** (removed).

**OBS!** to use diff on non-python projects we reccommend pushing a cached version (also refered to as the snapshot) to the branch, to optimise performance.

Make sure you are on a feature branch (not the base branch), then run:

```bash
archlens render-diff
```

This generates diagrams only for views that have actual changes. If there are no differences, a diagram without highlights is still generated.

Diff output indicates:

- **Green package/arrow** — added in the current branch
- **Red package/arrow** — removed in the current branch
- Count changes on arrows (e.g. `5 (+2)`)

## VSCode Extension

The [ArchLens for VSCode](https://github.com/archlens/ArchLens-VsCode-Extension) extension lets you generate and view architecture diagrams directly inside VS Code.

### Installation

Search for **ArchLens** in the VS Code Extensions panel and install it. You can also install it manually by downloading the `.vsix` file from the [releases page](https://github.com/archlens/ArchLens-VsCode-Extension/releases) and running:

```bash
code --install-extension archlens-<version>.vsix
```

The extension requires the **Python extension for VS Code** to be installed.

### Setup

1. Open the command palette (`Ctrl+Shift+P`).
2. Run the **ArchLens: Setup** command and follow the prompts.

The extension will guide you through creating your `archlens.json` configuration if one does not exist.

### Views in the extension

The extension displays three states:

- **Normal view**: your current architecture diagram
- **Diff view**: dependency changes compared to the base branch
- **Busy view**: while ArchLens is processing

### Using the local ArchLens build with the extension

If you are using the local development version of ArchLens (e.g. for C# support), install it into the virtual environment that the extension uses:

```bash
pip install -e "<path-to-local-archlens>/src/python"
```

## Multi-Language Support and Better Performance

The PyPi package (`archlens`) currently supports **Python projects only**. For multilanguage support and improved performance, you need to use the local build.

### Setup steps

1. **Clone the ArchLens repository:**

    ```bash
    git clone https://github.com/archlens/ArchLens
    ```

2. **Clone your target project** (if not already available locally).

3. **In your project, create and activate a virtual environment:**

    ```bash
    python -m venv .venv
    # Windows
    .venv\Scripts\activate
    # macOS/Linux
    source .venv/bin/activate
    ```

    > Use Python 3.10 or 3.12. Do **not** use Python 3.14.

4. **Install ArchLens from the local source:**

    ```bash
    pip install -e "<path-to-local-archlens>/src/python"
    ```

5. **Build the .NET component**:

    ```bash
    dotnet build
    ```

    ```bash
    dotnet publish "<path-to-archlens>/src/c-sharp/Archlens.csproj" \
        -o "<path-to-archlens>/src/python/src/.dotnet"
    ```

    This publishes the .NET build output, including the ArchLens DLL, into the `.dotnet` folder so the Python CLI can invoke it.

6. **Run ArchLens in your project:**

    ```bash
    archlens init
    archlens render
    ```

> **Note:** Any time you make changes to the C# source, re-run the `dotnet publish` command to update the DLL.
