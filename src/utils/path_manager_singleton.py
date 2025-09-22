from pathlib import Path

from src.utils.config_manager_singleton import ConfigManagerSingleton


class PathManagerSingleton:
    _instance = None

    _config_path = None
    _config_root_folder_path = None

    _git_config_path = None
    _git_root_folder_path = None

    def __new__(cls):
        if cls._instance is None:
            cls._instance = super().__new__(cls)
        return cls._instance

    def setup(self, config_manager: ConfigManagerSingleton, git_config=None):
        self._set_path(config_manager, git_config=False)
        self._set_path(git_config, git_config=True)

    def get_relative_path_from_project_root(self, path: str, append_root_folder=False):
        if self._config_path is None:
            raise Exception("Should call setup method before any other function")
        try:
            current_config_path = (
                self._config_root_folder_path
                if append_root_folder
                else self._config_path
            )
            return Path(path).relative_to(current_config_path).as_posix()
        except Exception:
            current_config_path = (
                self._git_root_folder_path
                if append_root_folder
                else self._git_config_path
            )
            return Path(path).relative_to(current_config_path).as_posix()

    def _set_path(self, config_manager: ConfigManagerSingleton, git_config: bool):
        if config_manager is None:
            return None
        config_path = Path(config_manager._config_path)
        config_root_folder_path = config_path.joinpath(config_manager.root_folder)

        config_path = config_path.as_posix()
        config_root_folder_path = config_root_folder_path.as_posix()

        if not git_config:
            self._config_path = config_path
            self._config_root_folder_path = config_root_folder_path
        else:
            self._git_config_path = config_path
            self._git_root_folder_path = config_root_folder_path
