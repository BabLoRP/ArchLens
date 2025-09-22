class ConfigManagerSingleton:
    _instance = None

    name = None
    root_folder = None
    github = None
    save_location = None
    views = None

    show_dependency_count = None
    package_color = None
    _config_path = None

    def __new__(cls):
        if cls._instance is None:
            cls._instance = super().__new__(cls)
        return cls._instance

    def setup(self, config: dict):
        self.name = config.get("name", "ArchLensProject")
        self.root_folder = config.get("rootFolder", "src")
        self.github = config.get("github")
        self.save_location = config["saveLocation"]
        self.views = config.get("views", [])

        self.show_dependency_count = config.get("showDependencyCount", True)
        self.package_color = config.get("packageColor", "#Azure")
        self._config_path = config["_config_path"]
