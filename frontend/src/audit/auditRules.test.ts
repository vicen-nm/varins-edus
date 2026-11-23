import { describe, expect, it } from 'vitest';
import { describeAction, shortId } from './auditRules';

describe('audit rules', () => {
  it('names the known actions in plain words', () => {
    expect(describeAction('auth.login.failed')).toBe('Intento de acceso fallido');
    expect(describeAction('card.scan.reused')).toBe('Código de carné reutilizado');
  });

  it('names the changes of a record from its entity and verb', () => {
    expect(describeAction('Vehicle.updated')).toBe('Vehículo: cambio');
    expect(describeAction('Visit.created')).toBe('Visita: creación');
    expect(describeAction('SomethingNew.deleted')).toBe('SomethingNew: eliminación');
  });

  it('keeps an unknown action as it is and shortens ids', () => {
    expect(describeAction('something.else')).toBe('something.else');
    expect(shortId('1935abfb-f20b-434b-ab10-66b46dbf29ba')).toBe('1935abfb');
    expect(shortId(null)).toBe('—');
  });
});