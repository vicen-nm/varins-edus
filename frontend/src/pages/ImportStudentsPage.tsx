import { useState } from 'react';
import type { ChangeEvent } from 'react';
import { Link } from 'react-router';
import { apiFetch } from '../api/client';
import { errorMessage, rowErrorMessage } from '../api/errors';
import type { ImportPreview, ImportResult } from '../api/types';
import { useAuth } from '../auth/useAuth';
import { buildCodesCsv } from '../import/codesCsv';

const FIELDS = [
  { key: 'identificationNumber', label: 'Cédula' },
  { key: 'firstName', label: 'Nombre' },
  { key: 'lastName', label: 'Apellidos' },
  { key: 'group', label: 'Grupo o sección' },
  { key: 'phone', label: 'Teléfono' },
  { key: 'email', label: 'Correo' },
] as const;

type Mapping = Record<string, string>;

// A stable text for a mapping, to know whether it changed since the last preview.
const signature = (m: Mapping) => JSON.stringify(FIELDS.map((f) => m[f.key] ?? ''));

const clean = (m: Mapping): Mapping =>
  Object.fromEntries(Object.entries(m).filter(([, column]) => column !== ''));

function buildForm(file: File, mapping: Mapping | null): FormData {
  const form = new FormData();
  form.append('file', file);

  if (mapping) {
    form.append('mapping', JSON.stringify(clean(mapping)));
  }

  return form;
}

function downloadCsv(result: ImportResult) {
  // The BOM makes Excel read the accents correctly.
  const blob = new Blob(['\uFEFF' + buildCodesCsv(result.codes)], { type: 'text/csv;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');

  link.href = url;
  link.download = 'codigos-activacion.csv';
  link.click();
  URL.revokeObjectURL(url);
}

export function ImportStudentsPage() {
  const { user } = useAuth();

  // Hiding things is only a convenience: the server enforces this permission.
  const allowed = Boolean(!user?.isPlatform && user?.permissions.includes('students.import'));

  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<ImportPreview | null>(null);
  const [mapping, setMapping] = useState<Mapping>({});
  const [applied, setApplied] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<ImportResult | null>(null);

  async function runPreview(target: File, used: Mapping | null) {
    setBusy(true);
    setError(null);

    try {
      const data = await apiFetch<ImportPreview>('/api/imports/students/preview', {
        method: 'POST',
        body: buildForm(target, used),
      });

      setPreview(data);
      setMapping(data.mapping);
      setApplied(signature(data.mapping));
    } catch (e) {
      setPreview(null);
      setError(errorMessage(e));
    } finally {
      setBusy(false);
    }
  }

  function handleFile(event: ChangeEvent<HTMLInputElement>) {
    const chosen = event.target.files?.[0] ?? null;

    // Lets the person choose the same file again after fixing it.
    event.target.value = '';

    if (!chosen) {
      return;
    }

    setFile(chosen);
    setPreview(null);
    void runPreview(chosen, null);
  }

  async function handleCommit() {
    if (!file) {
      return;
    }

    setBusy(true);
    setError(null);

    try {
      const data = await apiFetch<ImportResult>('/api/imports/students/commit', {
        method: 'POST',
        body: buildForm(file, mapping),
      });

      setResult(data);
      setFile(null);
      setPreview(null);
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusy(false);
    }
  }

  function reset() {
    // Leaving this screen discards the codes: they exist only in memory.
    setResult(null);
    setFile(null);
    setPreview(null);
    setError(null);
  }

  const summary = preview?.summary ?? null;
  const dirty = preview !== null && signature(mapping) !== applied;
  const canCommit = Boolean(summary && summary.newStudents > 0 && !dirty && !busy);
  const columns = (preview?.headers ?? []).filter((h) => h !== '');

  return (
    <main className="page">
      <header className="page__header no-print">
        <div>
          <p className="eyebrow">VARINS · EDU</p>
          <h1>Importar estudiantes</h1>
        </div>
        <Link to="/students" className="link">
          Volver a estudiantes
        </Link>
      </header>

      {!allowed && (
        <p role="alert" className="error">
          No tienes permiso para importar estudiantes.
        </p>
      )}

      {allowed && error && (
        <p role="alert" className="error no-print">
          {error}
        </p>
      )}

      {allowed && result && (
        <section className="card print-area" aria-labelledby="result-title">
          <h2 id="result-title">Importación terminada</h2>
          <p>
            Estudiantes nuevos: <strong>{result.created}</strong>. Ya existían: {result.alreadyExisted}. Filas con
            error (no importadas): {result.errorRows}.
          </p>

          {result.errors.length > 0 && (
            <ul className="no-print">
              {result.errors.map((e) => (
                <li key={`${e.rowNumber}-${e.code}`}>
                  Fila {e.rowNumber}: {rowErrorMessage(e.code)}
                </li>
              ))}
            </ul>
          )}

          {result.codes.length > 0 && (
            <>
              <p role="alert" className="error">
                Estos códigos se muestran una sola vez. Imprime o descarga la lista ahora: al salir de esta pantalla
                no se pueden recuperar (solo generar uno nuevo desde Estudiantes).
              </p>

              <div className="table-wrap">
                <table className="table">
                  <thead>
                    <tr>
                      <th scope="col">Cédula</th>
                      <th scope="col">Nombre</th>
                      <th scope="col">Grupo</th>
                      <th scope="col">Código</th>
                    </tr>
                  </thead>
                  <tbody>
                    {result.codes.map((c) => (
                      <tr key={c.identificationNumber}>
                        <td>{c.identificationNumber}</td>
                        <td>
                          {c.firstName} {c.lastName}
                        </td>
                        <td>{c.group ?? '—'}</td>
                        <td>
                          <code>{c.code}</code>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </>
          )}

          <div className="actions no-print">
            {result.codes.length > 0 && (
              <>
                <button type="button" onClick={() => window.print()}>
                  Imprimir
                </button>
                <button type="button" className="secondary" onClick={() => downloadCsv(result)}>
                  Descargar CSV
                </button>
              </>
            )}
            <button type="button" className="secondary" onClick={reset}>
              Terminar
            </button>
          </div>
        </section>
      )}

      {allowed && !result && (
        <>
          <section className="card">
            <label htmlFor="file">Archivo de Excel (.xlsx, máximo 5 MB)</label>
            <input
              id="file"
              type="file"
              accept=".xlsx"
              disabled={busy}
              onChange={handleFile}
            />
            {file && <p className="hint">Archivo: {file.name}</p>}
            {busy && !preview && <p className="hint">Leyendo el archivo…</p>}
          </section>

          {preview && (
            <section className="card" aria-labelledby="mapping-title">
              <h2 id="mapping-title">Columnas</h2>
              <p className="hint">Indica qué columna del Excel corresponde a cada dato.</p>

              <div className="filters">
                {FIELDS.map((f) => (
                  <div key={f.key}>
                    <label htmlFor={`map-${f.key}`}>{f.label}</label>
                    <select
                      id={`map-${f.key}`}
                      value={mapping[f.key] ?? ''}
                      onChange={(e) => setMapping({ ...mapping, [f.key]: e.target.value })}
                    >
                      <option value="">No usar</option>
                      {columns.map((h, i) => (
                        <option key={`${h}-${i}`} value={h}>
                          {h}
                        </option>
                      ))}
                    </select>
                  </div>
                ))}
              </div>

              {preview.missingFields.length > 0 && (
                <p role="alert" className="error">
                  Falta indicar la columna de: cédula, nombre y apellidos son obligatorios.
                </p>
              )}

              <div className="actions">
                <button type="button" className="secondary" disabled={busy || !file} onClick={() => file && runPreview(file, mapping)}>
                  Actualizar vista previa
                </button>
              </div>
            </section>
          )}

          {preview && summary && (
            <section className="card" aria-labelledby="preview-title">
              <h2 id="preview-title">Vista previa</h2>
              <p>
                Filas leídas: {preview.totalRows}. Estudiantes nuevos: <strong>{summary.newStudents}</strong>. Ya
                existen: {summary.existingStudents}. Filas con error: {summary.errorRows}.
              </p>

              {summary.groupsToCreate.length > 0 && (
                <p className="hint">Grupos que se crearán: {summary.groupsToCreate.join(', ')}.</p>
              )}

              {summary.errors.length > 0 && (
                <>
                  <h3>Filas con error (no se importarán)</h3>
                  <ul>
                    {summary.errors.map((e) => (
                      <li key={`${e.rowNumber}-${e.code}`}>
                        Fila {e.rowNumber}: {rowErrorMessage(e.code)}
                      </li>
                    ))}
                  </ul>
                </>
              )}

              {dirty && <p className="hint">Cambiaste las columnas: actualiza la vista previa antes de importar.</p>}

              <div className="actions">
                <button type="button" disabled={!canCommit} onClick={handleCommit}>
                  {busy ? 'Importando…' : `Importar ${summary.newStudents} estudiantes`}
                </button>
              </div>
            </section>
          )}
        </>
      )}
    </main>
  );
}