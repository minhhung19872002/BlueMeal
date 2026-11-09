import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import dayjs from 'dayjs';
import 'dayjs/locale/vi';
import 'dayjs/locale/en';

// Every JSON under locales/<lang>/<namespace>.json is a namespace (SPEC §9: namespace per module).
const modules = import.meta.glob('../../locales/*/*.json', { eager: true }) as Record<string, { default: Record<string, unknown> }>;

export const resources: Record<string, Record<string, Record<string, unknown>>> = {};
for (const [path, mod] of Object.entries(modules)) {
  const m = path.match(/locales\/([a-z]{2})\/([\w-]+)\.json$/);
  if (!m) continue;
  const [, lang, ns] = m;
  (resources[lang] ??= {})[ns] = mod.default;
}
export const namespaces = Object.keys(resources.vi ?? {});

export type Lang = 'vi' | 'en';
const LANG_KEY = 'bm.lang';

export function initialLanguage(): Lang {
  try {
    const v = localStorage.getItem(LANG_KEY);
    if (v === 'vi' || v === 'en') return v;
  } catch {
    /* storage unavailable */
  }
  return 'vi'; // default Vietnamese (SPEC §0.6)
}

void i18n.use(initReactI18next).init({
  resources,
  lng: initialLanguage(),
  fallbackLng: 'vi',
  ns: namespaces,
  defaultNS: 'common',
  interpolation: { escapeValue: false },
  returnNull: false,
});
dayjs.locale(i18n.language);

export function changeLanguage(lang: Lang) {
  try {
    localStorage.setItem(LANG_KEY, lang);
  } catch {
    /* ignore */
  }
  dayjs.locale(lang);
  document.documentElement.lang = lang;
  return i18n.changeLanguage(lang);
}

export default i18n;
