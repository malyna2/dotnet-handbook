#!/usr/bin/env python3
"""Extract the C# and Bicep samples from Chapter 50 so they can be compiled as printed.

Each ```csharp block is written to Generated/ChNN_BlockK.cs, wrapped according to BLOCKS below:
  "types"        the block declares types; it is placed in a namespace as-is
  "member:<Cls>" the block is a method; it is placed inside `partial class <Cls>` (Stubs.cs adds fields)
  "exercise:<f>" the block is a Find-the-bug sample; it must match code in verify/<f> token for token
                 (whitespace and // comments ignored), where the exercise tests run it
  "body:<args>"  the block is statements; it becomes the body of a method with those parameters
`using` lines are hoisted to the top of the file. Stubs.cs supplies the domain types the samples
assume (Order, ShopDbContext, …). Every ```bicep block is written to Generated/ChNN_BlockK.bicep.

A csharp block with no entry in BLOCKS fails the extraction, so a new sample cannot silently go
unverified. Run: python3 extract.py && dotnet build   (and `bicep build` on the .bicep files).
"""
import os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
CHAPTERS = os.path.join(HERE, "..", "..", "..", "chapters")
OUT = os.path.join(HERE, "Generated")

WEB = "WebApplicationBuilder builder, WebApplication app, TokenCredential credential"

# (chapter file, index among ```csharp blocks) → wrapping mode
BLOCKS = {
    ("50-azure-in-depth.md", 0): "body:string[] args",
    ("50-azure-in-depth.md", 1): "body:" + WEB,
    ("50-azure-in-depth.md", 2): "body:string[] args",
    ("50-azure-in-depth.md", 3): "types",
    ("50-azure-in-depth.md", 4): "member:Refunds",
    ("50-azure-in-depth.md", 5): "types",
    ("50-azure-in-depth.md", 6): "types",
    ("50-azure-in-depth.md", 7): "body:" + WEB,
    ("50-azure-in-depth.md", 8): "body:ServiceBusClient client, IPaymentService payments, ILogger logger",
    ("50-azure-in-depth.md", 9): "body:" + WEB,
    ("50-azure-in-depth.md", 10): "body:WebApplicationBuilder builder, TokenCredential credential",
    ("50-azure-in-depth.md", 11): "body:" + WEB,
}

COMMON_USINGS = """using Azure.Core;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
"""


def blocks(path, lang):
    text = open(path, encoding="utf-8").read()
    return [m.group(1) for m in re.finditer(r"^```" + lang + r"\n(.*?)^```", text, re.S | re.M)]


def wrap(code, mode, cls):
    lines = code.rstrip("\n").split("\n")
    usings = [l for l in lines if l.startswith("using ") and l.rstrip().endswith(";") and "(" not in l]
    body = "\n".join(l for l in lines if l not in usings)
    common = COMMON_USINGS.strip().split("\n")
    head = "\n".join(common + [u for u in usings if u not in common]) + "\n\nnamespace Snippets;\n\n"
    indented = "\n".join(("        " + l) if l.strip() else "" for l in body.split("\n"))
    if mode == "types":
        return head + body + "\n"
    if mode.startswith("member:"):
        return head + "public partial class %s\n{\n%s\n}\n" % (
            mode[len("member:"):], "\n".join(("    " + l) if l.strip() else "" for l in body.split("\n")))
    if mode.startswith("body:"):
        args = mode[len("body:"):]
        return head + "public static class %s\n{\n    public static async Task Run(%s)\n    {\n        await Task.CompletedTask;\n%s\n    }\n}\n" % (
            cls, args, indented)
    raise SystemExit("unknown mode " + mode)


def normalise(code):
    code = re.sub(r"//[^\n]*", "", code)
    return re.sub(r"\s+", "", code)


def check_exercise(chapter, i, code, rel):
    source = open(os.path.join(HERE, "..", "..", rel), encoding="utf-8").read()
    if normalise(code) not in normalise(source):
        sys.exit("%s csharp block %d does not match the tested code in verify/%s" % (chapter, i, rel))
    print("  %s block %d matches verify/%s" % (chapter, i, rel))


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        os.remove(os.path.join(OUT, f))
    chapters = sorted({c for c, _ in BLOCKS})
    missing, count = [], 0
    for chapter in chapters:
        nn = chapter[:2]
        for i, code in enumerate(blocks(os.path.join(CHAPTERS, chapter), "csharp")):
            mode = BLOCKS.get((chapter, i))
            if mode is None:
                missing.append("%s block %d" % (chapter, i))
                continue
            cls = "Ch%s_Block%d" % (nn, i)
            if mode.startswith("exercise:"):
                check_exercise(chapter, i, code, mode[len("exercise:"):])
                continue
            with open(os.path.join(OUT, "Ch%s_Block%d.cs" % (nn, i)), "w", encoding="utf-8") as out:
                out.write("// Extracted from chapters/%s, csharp block %d. Do not edit; edit the chapter.\n" % (chapter, i))
                out.write(wrap(code, mode, cls))
            count += 1
        for i, code in enumerate(blocks(os.path.join(CHAPTERS, chapter), "bicep")):
            with open(os.path.join(OUT, "Ch%s_Block%d.bicep" % (nn, i)), "w", encoding="utf-8") as out:
                out.write(code)
    if missing:
        sys.exit("No wrapping mode for: " + ", ".join(missing))
    print("Extracted %d C# blocks into %s" % (count, OUT))


if __name__ == "__main__":
    main()
