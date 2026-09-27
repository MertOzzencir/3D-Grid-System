# URP Lit.shader'dan vertex deformasyonlu kopyalar üretir (Village/Tree Chop Lit, Village/Wood Wobble Lit).
# Lit'ten tek fark: shader adı + 5 pass'te vertex, URP'nin kendi vertex fonksiyonundan önce deforme edilir.
# URP güncellenince tekrar çalıştır:  python "Assets/Shaders/Editor~/make_lit_variants.py"
# ("Editor~" klasörü ~ ile bittiği için Unity onu import etmez.)
import glob, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
SHADERS = os.path.dirname(HERE)
PROJECT = os.path.dirname(os.path.dirname(SHADERS))

VARIANTS = [
    # (shader adı, çıktı dosyası, include, uygulanan fonksiyon, açıklama)
    ("Village/Tree Chop Lit", "TreeChopLit.shader", "ChopDent.hlsl", "ApplyChopDent",
     "balta vuruşunda gövde göçüğü (ChopDent.hlsl)"),
    ("Village/Wood Wobble Lit", "WoodWobbleLit.shader", "Wobble.hlsl", "ApplyWobble",
     "birleşmede jöle gibi sallanma (Wobble.hlsl)"),
]

# (orijinal vertex, pass include'u, pozisyon alanı, normal alanı ya da None)
PASSES = [
    ("LitPassVertex",        "Shaders/LitForwardPass.hlsl",      "positionOS", "normalOS"),
    ("ShadowPassVertex",     "Shaders/ShadowCasterPass.hlsl",    "positionOS", "normalOS"),
    ("LitGBufferPassVertex", "Shaders/LitGBufferPass.hlsl",      "positionOS", "normalOS"),
    ("DepthOnlyVertex",      "Shaders/DepthOnlyPass.hlsl",       "position",   None),
    ("DepthNormalsVertex",   "Shaders/LitDepthNormalsPass.hlsl", "positionOS", "normal"),
]


def find_lit():
    matches = glob.glob(os.path.join(PROJECT, "Library", "PackageCache", "com.unity.render-pipelines.universal@*", "Shaders", "Lit.shader"))
    if len(matches) != 1:
        sys.exit(f"Lit.shader bulunamadı ya da birden fazla: {matches}")
    return matches[0]


def make_variant(lit, name, include, apply, description):
    header = (
        f"// URP Lit kopyası + {description}.\n"
        "// Lit'ten tek fark: ForwardLit, ShadowCaster, GBuffer, DepthOnly, DepthNormals pass'lerinde\n"
        f"// vertex, URP'nin kendi vertex fonksiyonuna gitmeden önce {apply} ile deforme edilir.\n"
        "// Elle düzenlenmez: Editor~/make_lit_variants.py ile üretilir, URP güncellenince yeniden üret.\n"
    )
    t = lit.replace('Shader "Universal Render Pipeline/Lit"', header + f'Shader "{name}"', 1)

    for original, pass_include, position, normal in PASSES:
        wrapper = "Village" + original
        pragma = f"#pragma vertex {original}"
        line = f'#include "Packages/com.unity.render-pipelines.universal/{pass_include}"'
        if t.count(pragma) != 1 or t.count(line) != 1:
            sys.exit(f"Lit.shader beklenen yapıda değil: {original}")

        args = f"input.{position}.xyz" + (f", input.{normal}" if normal else "")
        t = t.replace(pragma, f"#pragma vertex {wrapper}")
        t = t.replace(line, (
            f"{line}\n"
            f"            #include \"{include}\"\n"
            f"            Varyings {wrapper}(Attributes input)\n"
            f"            {{\n"
            f"                {apply}({args});\n"
            f"                return {original}(input);\n"
            f"            }}"
        ))
    return t


def main():
    lit_path = find_lit()
    lit = open(lit_path, encoding="utf-8").read().replace("\r\n", "\n")
    for name, output, include, apply, description in VARIANTS:
        path = os.path.join(SHADERS, output)
        open(path, "w", encoding="utf-8", newline="\n").write(make_variant(lit, name, include, apply, description))
        print("üretildi:", path)


if __name__ == "__main__":
    main()
