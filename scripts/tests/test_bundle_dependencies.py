import hashlib
import io
import os
from pathlib import Path
import shutil
import subprocess
import tarfile
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / "bundle-linux-dependencies.sh"


class DependencyBundleTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        scripts = self.root / "scripts"
        scripts.mkdir()
        self.script = scripts / SCRIPT.name
        shutil.copyfile(SCRIPT, self.script)
        self.archive = self.root / "artifacts/dependencies/7zip-fixture-linux-x64.tar.xz"
        self.archive.parent.mkdir(parents=True)
        with tarfile.open(self.archive, "w:xz") as archive:
            for name, value in {"7zzs": b"#!/bin/sh\nexit 0\n", "License.txt": b"fixture license", "readme.txt": b"fixture readme"}.items():
                member = tarfile.TarInfo(name)
                member.size = len(value)
                member.mode = 0o755 if name == "7zzs" else 0o644
                archive.addfile(member, io.BytesIO(value))
        checksum = hashlib.sha256(self.archive.read_bytes()).hexdigest()
        (scripts / "linux-dependencies.env").write_text(
            f"RHI_7ZIP_VERSION=fixture\nRHI_7ZIP_SHA256={checksum}\n"
            "RHI_7ZIP_URL=https://example.invalid/binary\nRHI_7ZIP_SOURCE_URL=https://example.invalid/source\n")
        self.publish = self.root / "publish"

    def test_verified_cached_archive_packages_binary_and_notices(self):
        subprocess.run(["/bin/bash", str(self.script), str(self.publish)], check=True, capture_output=True)
        self.assertTrue(os.access(self.publish / "tools/7zz", os.X_OK))
        self.assertEqual((self.publish / "licenses/7zip/License.txt").read_text(), "fixture license")
        self.assertIn("Source code: https://example.invalid/source", (self.publish / "licenses/7zip/BUILD-INFO.txt").read_text())

    def test_bad_cache_and_bad_download_cannot_be_packaged(self):
        self.archive.write_bytes(b"corrupted cache")
        # Simulate a completed but altered download without contacting any network.
        binaries = self.root / "bin"
        binaries.mkdir()
        curl = binaries / "curl"
        curl.write_text('#!/bin/bash\nwhile [[ "$1" != "-o" ]]; do shift; done\nprintf altered > "$2"\n')
        curl.chmod(0o755)
        result = subprocess.run(["/bin/bash", str(self.script), str(self.publish)],
                                env=dict(os.environ, PATH=str(binaries) + ":" + os.environ["PATH"]), capture_output=True, text=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("does not match the pinned SHA-256", result.stderr)
        self.assertFalse((self.publish / "tools/7zz").exists())
        self.assertEqual(list(self.archive.parent.glob("download.*")), [])


if __name__ == "__main__":
    unittest.main()
