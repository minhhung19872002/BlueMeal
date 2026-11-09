import js from '@eslint/js';
import globals from 'globals';
import reactHooks from 'eslint-plugin-react-hooks';
import tseslint from 'typescript-eslint';
import i18next from 'eslint-plugin-i18next';

export default tseslint.config(
  { ignores: ['dist', 'node_modules', 'playwright-report', 'test-results'] },
  {
    extends: [js.configs.recommended, ...tseslint.configs.recommended],
    files: ['src/**/*.{ts,tsx}'],
    languageOptions: { ecmaVersion: 2022, globals: globals.browser },
    plugins: { 'react-hooks': reactHooks, i18next },
    rules: {
      ...reactHooks.configs.recommended.rules,
      '@typescript-eslint/no-explicit-any': 'off',
      // SPEC §9: no hard-coded UI strings in JSX — every label must come from locales/vi|en
      'i18next/no-literal-string': ['error', {
        mode: 'jsx-only',
        'jsx-attributes': { include: ['title', 'placeholder', 'label', 'alt', 'aria-label', 'okText', 'cancelText', 'tooltip', 'description', 'message'] },
        callees: { exclude: ['t', 'i18n.t', '*.includes', 'includes', '*.startsWith', '*.endsWith', 'set*', '*.get', '*.set', '*.has', 'navigate', 'Number', 'String', 'console.*', 'Error', 'require', 'useTranslation', 'navigate', 'dayjs', 'fmt.*'] },
        words: { exclude: ['[0-9!-/:-@[-`{-~ ]+', '—', '·', '•', '→', '/', '₫', '%', 'kcal', 'g', 'P', 'L', 'G', 'VI', 'EN', 'BlueMeal', 'OK', 'Excel', 'MFA', 'TOTP', 'QR', 'API', 'P:L:G'] },
      }],
      // SPEC §10.5: XSS hardening
      'no-restricted-syntax': ['error', { selector: "JSXAttribute[name.name='dangerouslySetInnerHTML']", message: 'dangerouslySetInnerHTML is forbidden (SPEC §10.5).' }],
    },
  },
  {
    files: ['src/**/*.test.{ts,tsx}', 'src/test/**'],
    rules: { 'i18next/no-literal-string': 'off' },
  },
);
