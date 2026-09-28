import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / "install-linux-desktop.sh"


class DesktopInstallerTests(unittest.TestCase):
    def test_install_without_python_and_escape_desktop_fields(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            bundle = root / 'RHI 100% "quoted" $dollar `tick` \\slash'
            bundle.mkdir()
            installer = bundle / "install.sh"
            shutil.copyfile(SCRIPT, installer)
            (bundle / "RHI.Linux").write_text("unused fixture")
            (bundle / "RHI.Linux").chmod(0o755)
            minimal_bin = root / "bin"
            minimal_bin.mkdir()
            for name in ("dirname", "mkdir"):
                (minimal_bin / name).symlink_to(shutil.which(name))
            environment = dict(os.environ, PATH=str(minimal_bin), XDG_DATA_HOME=str(root / "data"))
            subprocess.run(["/bin/bash", str(installer)], env=environment, check=True, capture_output=True)
            entry = (root / "data/applications/rhi-linux.desktop").read_text()
            expected_name = r'RHI 100%% \\"quoted\\" \\$dollar \\`tick\\` \\\\slash'
            self.assertIn(f'Exec="{root}/{expected_name}/run-linux.sh"\n', entry)
            self.assertIn('Icon=' + str(bundle).replace('\\', '\\\\') + '/rhi.png\n', entry)
            self.assertIn('Terminal=false\n', entry)

    def test_newline_path_rejected_without_writing_desktop_entry(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            bundle = root / "RHI\nExec=unwanted"
            bundle.mkdir()
            shutil.copyfile(SCRIPT, bundle / "install.sh")
            (bundle / "RHI.Linux").touch(mode=0o755)
            environment = dict(os.environ, XDG_DATA_HOME=str(root / "data"))
            result = subprocess.run(["/bin/bash", str(bundle / "install.sh")], env=environment, capture_output=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertFalse((root / "data/applications/rhi-linux.desktop").exists())


if __name__ == "__main__":
    unittest.main()
