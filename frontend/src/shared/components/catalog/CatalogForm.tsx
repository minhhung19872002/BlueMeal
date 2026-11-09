import { Col, DatePicker, Form, Input, InputNumber, Row, Switch, TimePicker, Tooltip } from 'antd';
import { QuestionCircleOutlined } from '@ant-design/icons';
import type { FormInstance } from 'antd';
import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import { zodRule, required as requiredRule } from '@/shared/utils/validation';
import { DATE_FORMAT, parseVnNumber } from '@/shared/utils/format';
import type { FieldDef } from './types';
import { EnumSelect, LookupSelect } from './inputs';

const vnFormatter = (digits: number) => (v: number | string | undefined) => {
  if (v === undefined || v === null || v === '') return '';
  const n = typeof v === 'number' ? v : Number(v);
  if (Number.isNaN(n)) return String(v);
  return new Intl.NumberFormat('vi-VN', { maximumFractionDigits: digits }).format(n);
};

/** Renders a field definition list as a responsive antd form grid. */
export function CatalogFormFields({ fields, form }: { fields: FieldDef[]; form: FormInstance }) {
  const { t } = useTranslation();
  const values = Form.useWatch([], form) ?? {};
  return (
    <Row gutter={16}>
      {fields.map((f) => {
        if (f.hidden?.(values)) return null;
        const disabled = typeof f.disabled === 'function' ? f.disabled(values) : f.disabled;
        const rules = [...(f.required ? [requiredRule()] : []), ...(f.schema ? [zodRule(f.schema)] : [])];
        const label = f.tooltip ? (
          <span>
            {t(f.label)}{' '}
            <Tooltip title={t(f.tooltip)}>
              <QuestionCircleOutlined />
            </Tooltip>
          </span>
        ) : (
          t(f.label)
        );
        const span = f.span ?? (f.type === 'textarea' ? 24 : 12);
        const common = { name: f.name, label, rules, required: f.required };
        let input: React.ReactNode;
        switch (f.type) {
          case 'textarea':
            input = <Input.TextArea rows={3} maxLength={4000} showCount disabled={disabled} placeholder={f.placeholder && t(f.placeholder)} />;
            break;
          case 'number':
          case 'percent':
          case 'money':
            input = (
              <InputNumber
                style={{ width: '100%' }}
                min={f.min}
                max={f.max ?? (f.type === 'percent' ? 100 : undefined)}
                step={f.step ?? (f.type === 'money' ? 1000 : 1)}
                precision={f.precision ?? (f.type === 'money' ? 0 : undefined)}
                formatter={vnFormatter(f.precision ?? 4)}
                parser={(s) => parseVnNumber(s)}
                addonAfter={f.addonAfter ?? (f.type === 'percent' ? '%' : f.type === 'money' ? '₫' : undefined)}
                disabled={disabled}
                decimalSeparator=","
              />
            );
            break;
          case 'switch':
            return (
              <Col xs={24} md={span} key={f.name}>
                <Form.Item {...common} valuePropName="checked">
                  <Switch disabled={disabled} />
                </Form.Item>
              </Col>
            );
          case 'enum':
            input = <EnumSelect enumName={f.enumName!} values={f.enumValues ?? []} allowClear={!f.required} multiple={f.multiple} disabled={disabled} />;
            break;
          case 'lookup':
            input = <LookupSelect resource={f.lookup!} multiple={f.multiple} disabled={disabled} />;
            break;
          case 'date':
            return (
              <Col xs={24} md={span} key={f.name}>
                <Form.Item
                  {...common}
                  getValueProps={(v) => ({ value: v ? dayjs(v) : null })}
                  normalize={(v: dayjs.Dayjs | null) => (v ? v.format('YYYY-MM-DD') : null)}
                >
                  <DatePicker format={DATE_FORMAT} style={{ width: '100%' }} disabled={disabled} />
                </Form.Item>
              </Col>
            );
          case 'time':
            return (
              <Col xs={24} md={span} key={f.name}>
                <Form.Item
                  {...common}
                  getValueProps={(v) => ({ value: v ? dayjs('2000-01-01T' + v) : null })}
                  normalize={(v: dayjs.Dayjs | null) => (v ? v.format('HH:mm:ss') : null)}
                >
                  <TimePicker format="HH:mm" style={{ width: '100%' }} disabled={disabled} minuteStep={5} />
                </Form.Item>
              </Col>
            );
          case 'custom':
            return (
              <Col xs={24} md={span} key={f.name}>
                <Form.Item {...common}>
                  <CustomField def={f} values={values} />
                </Form.Item>
              </Col>
            );
          case 'email':
          case 'text':
          default:
            input = <Input disabled={disabled} maxLength={f.name === 'code' ? 50 : 500} placeholder={f.placeholder && t(f.placeholder)} />;
        }
        return (
          <Col xs={24} md={span} key={f.name}>
            <Form.Item {...common}>{input}</Form.Item>
          </Col>
        );
      })}
    </Row>
  );
}

function CustomField({ def, values, value, onChange }: { def: FieldDef; values: Record<string, unknown>; value?: unknown; onChange?: (v: unknown) => void }) {
  return <>{def.render?.({ value, onChange: (v) => onChange?.(v), values })}</>;
}
