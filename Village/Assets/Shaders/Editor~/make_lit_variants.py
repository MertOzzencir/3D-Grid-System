# URP Lit.shader'dan deformasyonlu / efektli kopyalar üretir (Village/Tree Chop Lit, Village/Wood Wobble Lit) ve düz
# kopyasını (Village/Lit: efekt yok, URP Lit kullanan materyaller bunu kullanır).
# Lit'ten farklar: shader adı + pass'lerde vertex (ve istenirse fragment), URP'nin kendi fonksiyonundan önce sarmalanır.
# Hepsinde ForwardLit fragment'ı Toon.hlsl'deki ToonLitPassFragment'a gider: toon kapalıyken (ToonStyle) birebir Lit.
# URP güncellenince tekrar çalıştır:  python "Assets/Shaders/Editor~/make_lit_variants.py"
# ("Editor~" klasörü ~ ile bittiği için Unity onu import etmez.)
import glob, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
SHADERS = os.path.dirname(HERE)
PROJECT = os.path.dirname(os.path.dirname(SHADERS))

VARIANTS = [
    dict(name="Village/Lit", output="VillageLit.shader",
         description="efekt yok (URP Lit kullanan materyaller bunu kullanır; toon denemesi için)",
         vertex=None, fragment=None),
    # name: shader adı, output: çıktı dosyası, description: başlık açıklaması
    # vertex: (include, fonksiyon) ya da None → pozisyon (ve normal) deforme edilir
    # fragment: (include, fonksiyon) ya da None → fonksiyon(input.positionCS) fragment'ın başında çağrılır (örn. clip)
    dict(name="Village/Tree Chop Lit", output="TreeChopLit.shader",
         description="balta vuruşunda gövde göçüğü (ChopDent.hlsl) + rüzgârda salınım (Wind.hlsl), ikisi TreeVertex.hlsl'de",
         vertex=("TreeVertex.hlsl", "ApplyTreeVertex"), fragment=None),
    dict(name="Village/Wood Wobble Lit", output="WoodWobbleLit.shader",
         description="birleşmede jöle gibi sallanma (Wobble.hlsl) + noktalı saydamlık (Fade.hlsl)",
         vertex=("Wobble.hlsl", "ApplyWobble"), fragment=("Fade.hlsl", "ApplyFade")),
    dict(name="Village/Creature Jiggle Lit", output="CreatureJiggleLit.shader",
         description="vuruşta yerel jöle titreşimi, örn. kediye şaplak (Jiggle.hlsl)",
         vertex=("Jiggle.hlsl", "ApplyJiggle"), fragment=None),
]

# (orijinal vertex, pass include'u, pozisyon alanı, normal alanı ya da None)
VERTEX_PASSES = [
    ("LitPassVertex",        "Shaders/LitForwardPass.hlsl",      "positionOS", "normalOS"),
    ("ShadowPassVertex",     "Shaders/ShadowCasterPass.hlsl",    "positionOS", "normalOS"),
    ("LitGBufferPassVertex", "Shaders/LitGBufferPass.hlsl",      "positionOS", "normalOS"),
    ("DepthOnlyVertex",      "Shaders/DepthOnlyPass.hlsl",       "position",   None),
    ("DepthNormalsVertex",   "Shaders/LitDepthNormalsPass.hlsl", "positionOS", "normal"),
]

# Fragment sarmalayıcıları. ShadowCaster bilerek yok: saydamlaşan objenin gölgesi tam kalır.
# Derinlik pass'leri renkle aynı pikselleri atmalı, yoksa atılan piksellerde arkadaki obje çizilemez.
# (orijinal fragment, pass include'u, pass dosyasında birebir bulunması gereken imza, sarmalayıcı şablonu)
# Şablonda {name} = sarmalayıcı adı, {call} = efekt çağrısı.
FRAGMENT_PASSES = [
    ("LitPassFragment", "Shaders/LitForwardPass.hlsl",
     "void LitPassFragment(\n"
     "    Varyings input\n"
     "    , out half4 outColor : SV_Target0\n"
     "#ifdef _WRITE_RENDERING_LAYERS\n"
     "    , out uint outRenderingLayers : SV_Target1\n"
     "#endif\n"
     ")",
     "void {name}(\n"
     "                Varyings input\n"
     "                , out half4 outColor : SV_Target0\n"
     "            #ifdef _WRITE_RENDERING_LAYERS\n"
     "                , out uint outRenderingLayers : SV_Target1\n"
     "            #endif\n"
     "            )\n"
     "            {{\n"
     "                {call}\n"
     "                LitPassFragment(input, outColor\n"
     "            #ifdef _WRITE_RENDERING_LAYERS\n"
     "                    , outRenderingLayers\n"
     "            #endif\n"
     "                );\n"
     "            }}"),
    ("LitGBufferPassFragment", "Shaders/LitGBufferPass.hlsl",
     "GBufferFragOutput LitGBufferPassFragment(Varyings input)",
     "GBufferFragOutput {name}(Varyings input)\n"
     "            {{\n"
     "                {call}\n"
     "                return LitGBufferPassFragment(input);\n"
     "            }}"),
    ("DepthOnlyFragment", "Shaders/DepthOnlyPass.hlsl",
     "half DepthOnlyFragment(Varyings input) : SV_TARGET",
     "half {name}(Varyings input) : SV_TARGET\n"
     "            {{\n"
     "                {call}\n"
     "                return DepthOnlyFragment(input);\n"
     "            }}"),
    ("DepthNormalsFragment", "Shaders/LitDepthNormalsPass.hlsl",
     "void DepthNormalsFragment(\n"
     "    Varyings input\n"
     "    , out half4 outNormalWS : SV_Target0\n"
     "#ifdef _WRITE_RENDERING_LAYERS\n"
     "    , out uint outRenderingLayers : SV_Target1\n"
     "#endif\n"
     ")",
     "void {name}(\n"
     "                Varyings input\n"
     "                , out half4 outNormalWS : SV_Target0\n"
     "            #ifdef _WRITE_RENDERING_LAYERS\n"
     "                , out uint outRenderingLayers : SV_Target1\n"
     "            #endif\n"
     "            )\n"
     "            {{\n"
     "                {call}\n"
     "                DepthNormalsFragment(input, outNormalWS\n"
     "            #ifdef _WRITE_RENDERING_LAYERS\n"
     "                    , outRenderingLayers\n"
     "            #endif\n"
     "                );\n"
     "            }}"),
]


def find_urp():
    matches = glob.glob(os.path.join(PROJECT, "Library", "PackageCache", "com.unity.render-pipelines.universal@*"))
    if len(matches) != 1:
        sys.exit(f"URP paketi bulunamadı ya da birden fazla: {matches}")
    return matches[0]


def read(path):
    return open(path, encoding="utf-8").read().replace("\r\n", "\n")


def include_line(pass_include):
    return f'#include "Packages/com.unity.render-pipelines.universal/{pass_include}"'


def make_variant(urp, lit, v):
    header = f"// URP Lit kopyası + {v['description']}.\n"
    if v["vertex"]:
        header += (
            "// ForwardLit, ShadowCaster, GBuffer, DepthOnly, DepthNormals pass'lerinde\n"
            f"// vertex, URP'nin kendi vertex fonksiyonuna gitmeden önce {v['vertex'][1]} ile deforme edilir.\n"
        )
    header += ("// ForwardLit fragment'ı Toon.hlsl'deki ToonLitPassFragment: toon kapalıyken (ToonStyle) birebir Lit, "
               "açıkken sert geçişli ışık.\n")
    if v["fragment"]:
        header += (f"// ForwardLit, GBuffer, DepthOnly, DepthNormals fragment'ı önce {v['fragment'][1]} çağırır "
                   "(ShadowCaster hariç).\n")
    header += "// Elle düzenlenmez: Editor~/make_lit_variants.py ile üretilir, URP güncellenince yeniden üret.\n"
    t = lit.replace('Shader "Universal Render Pipeline/Lit"', header + f'Shader "{v["name"]}"', 1)

    # Her pass include'unun hemen arkasına eklenecek kod
    additions = {}

    for original, pass_include, position, normal in (VERTEX_PASSES if v["vertex"] else []):
        vertex_include, vertex_apply = v["vertex"]
        wrapper = "Village" + original
        pragma = f"#pragma vertex {original}"
        if t.count(pragma) != 1 or t.count(include_line(pass_include)) != 1:
            sys.exit(f"Lit.shader beklenen yapıda değil: {original}")

        args = f"input.{position}.xyz" + (f", input.{normal}" if normal else "")
        t = t.replace(pragma, f"#pragma vertex {wrapper}")
        additions.setdefault(pass_include, []).append(
            f"#include \"{vertex_include}\"\n"
            f"            Varyings {wrapper}(Attributes input)\n"
            f"            {{\n"
            f"                {vertex_apply}({args});\n"
            f"                return {original}(input);\n"
            f"            }}")

    # ForwardLit her zaman sarmalanır (toon), diğer pass'ler sadece fragment efekti varsa
    for original, pass_include, signature, template in FRAGMENT_PASSES:
        forward = original == "LitPassFragment"
        if v["fragment"] or forward:
            wrapper = "Village" + original
            pragma = f"#pragma fragment {original}"
            if t.count(pragma) != 1:
                sys.exit(f"Lit.shader beklenen yapıda değil: {original}")
            if read(os.path.join(urp, pass_include)).count(signature) != 1:
                sys.exit(f"{pass_include} içinde {original} imzası değişmiş, FRAGMENT_PASSES'ı güncelle")

            t = t.replace(pragma, f"#pragma fragment {wrapper}")
            includes = ""
            if v["fragment"]:
                fragment_include, fragment_apply = v["fragment"]
                includes += f"#include \"{fragment_include}\"\n            "
                body = template.format(name=wrapper, call=f"{fragment_apply}(input.positionCS);")
            else:
                body = template.format(name=wrapper, call="@@").replace("                @@\n", "")
            if forward:
                includes += "#include \"Toon.hlsl\"\n            "
                body = body.replace("                LitPassFragment(input", "                ToonLitPassFragment(input")
            additions.setdefault(pass_include, []).append(includes + body)

    for pass_include, blocks in additions.items():
        line = include_line(pass_include)
        t = t.replace(line, line + "".join("\n            " + block for block in blocks))
    # DepthNormals pass'i normal map'siz: ekran normalleri (toon konturu, SSAO) modelin gerçek şeklinden gelir. Normal map'in
    # oyukları uzakta birkaç piksele sıkışıp kontura keskin köşe gibi görünüyor, tırtıklı koyu çizgiler çıkıyordu.
    start = t.index('Name "DepthNormals"')
    end = t.index("ENDHLSL", start)
    pragma = "            #pragma shader_feature_local _NORMALMAP\n"
    if t.count(pragma, start, end) != 1:
        sys.exit("Lit.shader beklenen yapıda değil: DepthNormals _NORMALMAP")
    t = t[:start] + t[start:end].replace(pragma, "            // _NORMALMAP yok: ekran normalleri modelin şeklinden (toon konturu)\n") + t[end:]
    header_note = "// DepthNormals pass'i normal map'siz (toon konturu modelin şeklinden çizilsin).\n"
    t = t.replace("// Elle düzenlenmez:", header_note + "// Elle düzenlenmez:", 1)
    return t


def main():
    urp = find_urp()
    lit = read(os.path.join(urp, "Shaders", "Lit.shader"))
    for v in VARIANTS:
        path = os.path.join(SHADERS, v["output"])
        open(path, "w", encoding="utf-8", newline="\n").write(make_variant(urp, lit, v))
        print("üretildi:", path)


if __name__ == "__main__":
    main()
