"""The shared test vectors in spec/translation-file.json (translation files, supertext.files)."""

from __future__ import annotations

import dataclasses
from collections.abc import Callable
from typing import Any

import pytest
from conftest import spec

from supertext import files
from supertext.files import (
    MAX_FILE_CHARACTERS,
    FileJob,
    FileSegment,
    TranslationFile,
    TranslationFileError,
)
from supertext.v1 import Segment

VECTORS = spec("translation-file.json")


def segment(data: dict[str, Any]) -> FileSegment:
    return FileSegment(data["key"], data["text"], data.get("html", False))


def job(data: dict[str, Any]) -> FileJob:
    return FileJob(
        data["plugin"],
        data["reference"],
        data["sourceLanguage"],
        data["targetLanguage"],
        [segment(s) for s in data["segments"]],
    )


def as_json(file: TranslationFile) -> dict[str, Any]:
    return {
        "format": file.format,
        "plugin": file.plugin,
        "reference": file.reference,
        "sourceLanguage": file.source_language,
        "targetLanguage": file.target_language,
        "units": [{"key": u.key, "checksum": u.checksum} for u in file.units],
    }


def error_code(fn: Callable[[], object]) -> str | None:
    try:
        fn()
    except TranslationFileError as e:
        return e.code
    return None


def name(case: dict[str, Any]) -> str:
    return str(case["name"])


@pytest.mark.parametrize("case", VECTORS["checksum"], ids=lambda c: repr(c["segment"]))
def test_checksum(case: dict[str, Any]) -> None:
    data = case["segment"]
    assert files.checksum(FileSegment("k", data["text"], data.get("html", False))) == case["out"]
    assert files.checksum(Segment(data["text"], data.get("html", False))) == case["out"]


@pytest.mark.parametrize("case", VECTORS["exportXliff"], ids=name)
def test_export_xliff(case: dict[str, Any]) -> None:
    jobs = [job(j) for j in case["jobs"]]
    if "error" in case:
        assert error_code(lambda: files.export_xliff(jobs)) == case["error"]
    else:
        assert files.export_xliff(jobs) == case["out"]


@pytest.mark.parametrize("case", VECTORS["exportHtml"], ids=name)
def test_export_html(case: dict[str, Any]) -> None:
    the_job = job(case["job"])
    if "error" in case:
        assert error_code(lambda: files.export_html(the_job)) == case["error"]
    else:
        assert files.export_html(the_job) == case["out"]


@pytest.mark.parametrize("case", VECTORS["read"], ids=name)
def test_read(case: dict[str, Any]) -> None:
    content: str = case["repeat"]["text"] * case["repeat"]["times"] if "repeat" in case else case["content"]
    if "error" in case:
        assert error_code(lambda: files.read(content)) == case["error"]
    else:
        assert [as_json(f) for f in files.read(content)] == case["files"]


@pytest.mark.parametrize("case", VECTORS["import"], ids=name)
def test_import(case: dict[str, Any]) -> None:
    file = files.read(case["content"])[case.get("file", 0)]
    result = files.import_translations(file, [segment(s) for s in case["current"]])
    assert [dataclasses.asdict(r) for r in result] == case["out"]


def test_a_file_not_returned_by_read_is_refused() -> None:
    file = TranslationFile("html", "P", "r", "de", "fr", ())
    assert error_code(lambda: files.import_translations(file, [])) == "invalid_file"


def test_values_are_immutable_and_errors_carry_the_code() -> None:
    assert MAX_FILE_CHARACTERS == 10_000_000
    file = files.read(VECTORS["read"][0]["content"])[0]
    with pytest.raises(dataclasses.FrozenInstanceError):
        file.plugin = "x"  # type: ignore[misc]
    assert "_units" not in repr(file)
    error = TranslationFileError("invalid_file", "bad")
    assert (error.code, str(error)) == ("invalid_file", "bad")


def test_hostile_input_does_not_crash() -> None:
    deep = "<?xml version='1.0'?><xliff><file original='r' source-language='de' target-language='fr' product-name='P'>"
    deep += "<body>" + "<group>" * 5000 + "<trans-unit id='a'><source>x</source><target>"
    deep += "<mrk>" * 5000 + "y" + "</mrk>" * 5000 + "</target></trans-unit>" + "</group>" * 5000
    deep += "</body></file></xliff>"
    (file,) = files.read(deep)
    assert files.import_translations(file, [FileSegment("a", "x")])[0].text == "y"
    html = files.export_html(FileJob("P", "r", "de", "fr", [FileSegment("a", "x", html=True)]))
    html = html.replace('content="0 ', 'content="999999999 ').replace(
        "</body>", '<p data-st-id="' + "9" * 5000 + '">x</p></body>'
    )
    (file,) = files.read(html)
    assert files.import_translations(file, [FileSegment("a", "x", html=True)])[0].status == "untranslated"
    link = "&#" + "1" * 5000 + ";"
    (file,) = files.read(
        files.export_html(FileJob("P", "r", "de", "fr", [FileSegment("a", f'<a href="{link}">x</a>', html=True)]))
    )
    assert (
        files.import_translations(file, [FileSegment("a", f'<a href="{link}">x</a>', html=True)])[0].status
        == "invalid_markup"
    )
