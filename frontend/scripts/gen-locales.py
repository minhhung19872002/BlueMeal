# Generates src/locales/{vi,en}/<ns>.json from bilingual sources in scripts/locale-src/<ns>.py.
# Each source defines L = { key: ("vi text", "en text") | { nested } }. Keeps both languages in sync by construction.
import json, os, sys

here = os.path.dirname(os.path.abspath(__file__))
src = os.path.join(here, "locale-src")
root = os.path.join(here, "..", "src", "locales")


def split(d, idx):
    return {k: (split(v, idx) if isinstance(v, dict) else v[idx]) for k, v in d.items()}


count = 0
for f in sorted(os.listdir(src)):
    if not f.endswith(".py") or f.startswith("_"):
        continue
    ns = f[:-3]
    env = {}
    exec(open(os.path.join(src, f), encoding="utf-8").read(), env)
    data = env["L"]
    for i, lang in enumerate(["vi", "en"]):
        os.makedirs(os.path.join(root, lang), exist_ok=True)
        with open(os.path.join(root, lang, ns + ".json"), "w", encoding="utf-8") as fp:
            json.dump(split(data, i), fp, ensure_ascii=False, indent=2)
            fp.write("\n")
    count += 1
print(f"generated {count} namespace(s)")
