import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / "check-upstream-sync.py"
SOURCE = "RenoDXCommander/Services/ExampleService.cs"


class UpstreamReviewTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.git("init", "-q")
        self.git("config", "user.name", "Fixture")
        self.git("config", "user.email", "fixture@example.invalid")
        self.write(SOURCE, "class ExampleService {}\n")
        self.write("RHI.Linux.Core/Example.cs", "class Example {}\n")
        self.git("add", ".")
        self.git("commit", "-qm", "initial upstream fixture")
        commit = self.git("rev-parse", "HEAD").strip()
        self.git("branch", "upstream-fixture")
        checksum = hashlib.sha256((self.root / SOURCE).read_bytes()).hexdigest()
        self.manifest = {"version": 1, "reviewed_commit": commit, "files": {SOURCE: {
            "status": "adapted", "linux": ["RHI.Linux.Core/Example.cs"],
            "reason": "Uses Linux paths instead of registry locations.",
            "local_sha256": checksum, "upstream_sha256": checksum}}}
        self.save()

    def git(self, *args):
        return subprocess.check_output(["git", "-C", str(self.root), *args], text=True)

    def write(self, path, text):
        target = self.root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(text)

    def save(self):
        self.write("docs/LINUX-PORT-SYNC.json", json.dumps(self.manifest))

    def check(self, success, upstream="upstream-fixture"):
        result = subprocess.run([sys.executable, str(SCRIPT), "--repo", str(self.root), "--upstream", upstream],
                                text=True, capture_output=True)
        self.assertEqual(result.returncode, 0 if success else 1, result.stdout + result.stderr)
        return result.stdout + result.stderr

    def test_reviewed_baseline_passes(self):
        self.check(True)

    def test_local_edit_requires_review(self):
        self.write(SOURCE, "class ExampleService { int Changed; }\n")
        self.assertIn("LOCAL SOURCE CHANGED", self.check(False))

    def test_new_source_requires_mapping_even_before_git_add(self):
        self.write("RenoDXCommander/Services/NewService.cs", "class NewService {}")
        self.assertIn("UNMAPPED", self.check(False))

    def test_new_models_ui_shared_code_and_data_require_mapping(self):
        paths = [
            "RenoDXCommander/Models/NewModel.cs",
            "RenoDXCommander/ViewModels/NewViewModel.cs",
            "RenoDXCommander/NewWindow.xaml",
            "RHI.Core/NewPolicy.cs",
            "database/new-catalogue.json",
            "engine-files/new-game.ini",
        ]
        for path in paths:
            with self.subTest(path=path):
                self.write(path, "new production input")
                output = self.check(False)
                self.assertIn("UNMAPPED: " + path, output)
                (self.root / path).unlink()

    def test_deleted_source_requires_review(self):
        (self.root / SOURCE).unlink()
        self.assertIn("LOCAL SOURCE CHANGED", self.check(False))

    def test_new_upstream_commit_detected_without_merging(self):
        self.write(SOURCE, "class ExampleService { int UpstreamFix; }\n")
        self.git("add", SOURCE)
        self.git("commit", "-qm", "upstream fix")
        self.git("branch", "-f", "upstream-fixture", "HEAD")
        self.git("checkout", "-q", "HEAD~1", "--", SOURCE)
        output = self.check(False)
        self.assertIn("UPSTREAM SOURCE CHANGED", output)
        self.assertNotIn("LOCAL SOURCE CHANGED", output)

    def test_deleted_upstream_source_detected(self):
        self.git("rm", "-q", SOURCE)
        self.git("commit", "-qm", "upstream removal")
        self.git("branch", "-f", "upstream-fixture", "HEAD")
        self.git("checkout", "-q", "HEAD~1", "--", SOURCE)
        self.assertIn("UPSTREAM SOURCE CHANGED", self.check(False))

    def test_new_upstream_source_detected_without_merging(self):
        path = "RenoDXCommander/Services/NewUpstreamService.cs"
        self.write(path, "class NewUpstreamService {}")
        self.git("add", path)
        self.git("commit", "-qm", "new upstream feature")
        self.git("branch", "-f", "upstream-fixture", "HEAD")
        (self.root / path).unlink()
        self.assertIn("UNMAPPED", self.check(False))

    def test_no_empty_reason_or_missing_counterpart(self):
        self.manifest["files"][SOURCE]["reason"] = ""
        self.manifest["files"][SOURCE]["linux"] = ["RHI.Linux.Core/Missing.cs"]
        self.save()
        output = self.check(False)
        self.assertIn("INVALID DISPOSITION", output)
        self.assertIn("MISSING/INVALID COUNTERPART", output)

    def test_deferred_features_also_require_review_after_change(self):
        self.manifest["files"][SOURCE].update(status="deferred", linux=[], reason="Not yet ported; tracked explicitly.")
        self.save()
        self.check(True)
        self.write(SOURCE, "class ExampleService { int NewPortableFeature; }")
        self.assertIn("LOCAL SOURCE CHANGED", self.check(False))

    def test_missing_upstream_ref_fails_closed(self):
        self.assertIn("could not run", self.check(False, "nonexistent-ref"))


if __name__ == "__main__":
    unittest.main()
