import os
import pty
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / "install-linux-desktop.sh"


class DesktopInstallerTests(unittest.TestCase):
    def test_interactive_desktop_choice(self):
        for answer, expected in (("Y", True), ("y", True), ("N", False), ("", False)):
            with self.subTest(answer=answer), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                bundle = root / "bundle"
                bundle.mkdir()
                shutil.copyfile(SCRIPT, bundle / "install.sh")
                (bundle / "RHI.Linux").touch(mode=0o755)
                desktop = root / "Localized Desktop"
                bin_dir = root / "bin"
                bin_dir.mkdir()
                xdg = bin_dir / "xdg-user-dir"
                xdg.write_text('#!/bin/sh\nprintf "%s\\n" "$RHI_TEST_DESKTOP"\n')
                xdg.chmod(0o755)
                environment = dict(os.environ, PATH=str(bin_dir) + os.pathsep + os.environ["PATH"],
                                   XDG_DATA_HOME=str(root / "data"), RHI_TEST_DESKTOP=str(desktop))
                master, slave = pty.openpty()
                try:
                    os.write(master, (answer + "\n").encode())
                    result = subprocess.run(["/bin/bash", str(bundle / "install.sh")], env=environment,
                                            stdin=slave, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=10)
                finally:
                    os.close(master)
                    os.close(slave)
                self.assertEqual(result.returncode, 0, result.stderr)
                entry = root / "data/applications/rhi-linux.desktop"
                self.assertTrue(entry.exists())
                shortcut = desktop / "rhi-linux.desktop"
                self.assertEqual(shortcut.exists(), expected)
                if expected:
                    self.assertEqual(shortcut.read_bytes(), entry.read_bytes())
                    self.assertTrue(os.access(shortcut, os.X_OK))

    def test_explicit_desktop_respects_disabled_desktop_directory(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            shutil.copyfile(SCRIPT, root / "install.sh")
            (root / "RHI.Linux").touch(mode=0o755)
            bin_dir = root / "bin"
            bin_dir.mkdir()
            xdg = bin_dir / "xdg-user-dir"
            xdg.write_text('#!/bin/sh\nprintf "%s\\n" "$HOME"\n')
            xdg.chmod(0o755)
            environment = dict(os.environ, PATH=str(bin_dir) + os.pathsep + os.environ["PATH"],
                               XDG_DATA_HOME=str(root / "data"))
            result = subprocess.run(["/bin/bash", str(root / "install.sh"), "--desktop"],
                                    env=environment, capture_output=True, text=True, check=True)
            self.assertIn("Desktop folder is disabled", result.stdout)

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
