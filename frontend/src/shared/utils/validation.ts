import { z } from 'zod';
import type { Rule } from 'antd/es/form';
import i18n from '@/shared/i18n';

/**
 * Bridges zod schemas to antd Form rules so every form validates with zod and shows bilingual messages (SPEC §9).
 * Messages are i18n keys resolved at validation time (so they follow the current language).
 */
export function zodRule(schema: z.ZodType): Rule {
  return {
    validator: async (_rule, value) => {
      const r = schema.safeParse(value === '' ? undefined : value);
      if (r.success) return;
      const issue = r.error.issues[0];
      throw new Error(translateIssue(issue));
    },
  };
}

function translateIssue(issue: z.core.$ZodIssue): string {
  const msg = issue.message;
  if (msg && msg.includes(':') && i18n.exists(msg.split('|')[0])) {
    const [key, json] = msg.split('|');
    return String(i18n.t(key, json ? JSON.parse(json) : undefined));
  }
  switch (issue.code) {
    case 'too_small':
      return issue.origin === 'string' ? i18n.t('common:validation.minLength', { min: issue.minimum }) : i18n.t('common:validation.min', { min: issue.minimum });
    case 'too_big':
      return issue.origin === 'string' ? i18n.t('common:validation.maxLength', { max: issue.maximum }) : i18n.t('common:validation.max', { max: issue.maximum });
    case 'invalid_type':
      return issue.input === undefined ? i18n.t('common:validation.requiredShort') : i18n.t('common:validation.number');
    case 'invalid_format':
      return issue.format === 'email' ? i18n.t('common:validation.email') : i18n.t('common:validation.requiredShort');
    default:
      return i18n.t('common:validation.requiredShort');
  }
}

/** Common building blocks. */
export const zs = {
  required: (max = 300) => z.string({ message: 'common:validation.requiredShort' }).trim().min(1, 'common:validation.requiredShort').max(max),
  optional: (max = 1000) => z.string().max(max).optional().nullable(),
  code: () => z.string({ message: 'common:validation.requiredShort' }).trim().min(1, 'common:validation.requiredShort').max(50).regex(/^[A-Za-z0-9_.\-/]+$/, 'catalog:validation.code'),
  email: () => z.email({ message: 'common:validation.email' }).optional().nullable(),
  number: (min?: number, max?: number) => {
    let s = z.number({ message: 'common:validation.number' });
    if (min !== undefined) s = s.min(min);
    if (max !== undefined) s = s.max(max);
    return s;
  },
  /** Password policy mirrors the server defaults (server remains authoritative). */
  password: () =>
    z
      .string({ message: 'common:validation.requiredShort' })
      .min(10, 'auth:changePassword.policy')
      .regex(/[A-Z]/, 'auth:changePassword.policy')
      .regex(/[a-z]/, 'auth:changePassword.policy')
      .regex(/\d/, 'auth:changePassword.policy')
      .regex(/[^A-Za-z0-9]/, 'auth:changePassword.policy'),
};

export const required = (): Rule => zodRule(z.any().refine((v) => v !== undefined && v !== null && v !== '', 'common:validation.requiredShort'));
