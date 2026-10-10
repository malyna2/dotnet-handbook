"""Build integration checks: run with python3 verify/site/build_test.py."""
import contextlib
import importlib.util
import io
import json
from pathlib import Path
import shutil
import unittest
import uuid
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("handbook_build", ROOT / "build_site.py")
builder = importlib.util.module_from_spec(spec)
spec.loader.exec_module(builder)

EN = "# Chapter 1: Example\n\n## A section\n\nEnglish `Task` prose.\n\n```csharp\nvar x = 1;\n```\n"
UK = "# Розділ 1: Приклад\n\n## Секція\n\nУкраїнський текст про `Task`.\n\n```csharp\nvar x = 1;\n```\n"


class BuildTest(unittest.TestCase):
    def setUp(self):
        self.root = (ROOT / ("handbook-build-test-" + uuid.uuid4().hex)).resolve()
        self.root.relative_to(ROOT)  # Validate the cleanup target stays in this workspace.
        self.root.mkdir()
        self.addCleanup(shutil.rmtree, self.root)
        self.chapters = self.root / "chapters"
        self.uk = self.chapters / "uk"
        self.uk.mkdir(parents=True)
        self.name = "101-example.md"
        (self.chapters / self.name).write_text(EN, encoding="utf-8")
        (self.uk / self.name).write_text(UK, encoding="utf-8")
        self.paths = patch.multiple(builder,
            CH_DIR=str(self.chapters), UK_DIR=str(self.uk),
            OUT_JS=str(self.root / "site/content.js"), OUT_MD=str(self.root / "main.md"),
            OUT_UK_JS=str(self.root / "site/content.uk.js"), OUT_UK_MD=str(self.root / "main.uk.md"),
            ALIASES_FILE=str(self.chapters / "_aliases.json"), TRANSLATION_STATE=str(self.uk / "_sources.json"))
        self.paths.start()
        self.addCleanup(self.paths.stop)

    def build(self):
        with contextlib.redirect_stdout(io.StringIO()):
            builder.build()

    def test_builds_both_editions_with_canonical_routes_and_localized_readtime(self):
        self.build()
        text = (self.root / "site/content.uk.js").read_text(encoding="utf-8")
        book = json.loads(text.removeprefix("window.BOOK_UK = ").removesuffix(";\n"))
        self.assertEqual(book[0]["slug"], "chapter-1-example")
        self.assertEqual(book[0]["anchors"], ["chapter-1-example", "a-section"])
        self.assertIn("Орієнтовний час читання", book[0]["md"])
        self.assertTrue((self.root / "main.uk.md").exists())

    def test_new_chapter_requires_translation_before_outputs_are_written(self):
        (self.chapters / "102-new.md").write_text("# Chapter 2: New\n", encoding="utf-8")
        with self.assertRaisesRegex(SystemExit, "102-new"):
            self.build()
        self.assertFalse((self.root / "site/content.js").exists())

    def test_source_edit_requires_a_translation_update(self):
        self.build()
        (self.chapters / self.name).write_text(EN + "\nNew section prose.\n", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "without updating"):
            self.build()
        (self.uk / self.name).write_text(UK + "\nНовий текст розділу.\n", encoding="utf-8")
        self.build()

    def test_translation_cannot_drop_headings_or_alter_tested_examples(self):
        for translated, error in [(UK.replace("var x = 1;", "var x = 2;"), "code examples"),
                                  (UK.replace("## Секція\n", ""), "heading levels")]:
            with self.subTest(error=error):
                (self.uk / self.name).write_text(translated, encoding="utf-8")
                with self.assertRaisesRegex(ValueError, error):
                    self.build()

    def test_partly_translated_source_copy_cannot_ship(self):
        sentence = "This unchanged English teaching sentence must also be translated into Ukrainian.\n"
        (self.chapters / self.name).write_text(EN + sentence, encoding="utf-8")
        (self.uk / self.name).write_text(UK + sentence, encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "untranslated English prose"):
            self.build()

    def test_quoted_code_examples_must_also_be_preserved(self):
        quoted = "\n> ```csharp\n> var quoted = 1;\n> ```\n"
        (self.chapters / self.name).write_text(EN + quoted, encoding="utf-8")
        (self.uk / self.name).write_text(UK + quoted, encoding="utf-8")
        self.build()
        (self.uk / self.name).write_text(UK + quoted.replace("quoted = 1", "quoted = 2"), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "code examples"):
            self.build()


if __name__ == "__main__":
    unittest.main()
