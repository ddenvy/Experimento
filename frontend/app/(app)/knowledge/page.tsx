"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { api, type KnowledgeDocumentDto, type ProjectDto, type SearchResultDto } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Search, Loader2, Upload, FileText, X } from "lucide-react";

const SOURCE_TYPES = ["Patent", "Paper", "InternalExperiment"] as const;
const MAX_CONTENT = 200_000;
const MAX_FILE_BYTES = 10 * 1024 * 1024;
const ACCEPTED_EXTENSIONS = [".pdf", ".docx", ".txt", ".md", ".csv", ".xls", ".xlsx"];

// Цвет бейджа по статусу индексации.
function StatusBadge({ status }: { status: string }) {
  const styles: Record<string, string> = {
    Pending: "border-yellow-400 text-yellow-700 bg-yellow-50 dark:bg-yellow-950/30",
    Processing: "border-blue-400 text-blue-700 bg-blue-50 dark:bg-blue-950/30",
    Ready: "border-green-500 text-green-700 bg-green-50 dark:bg-green-950/30",
    Failed: "border-red-500 text-red-700 bg-red-50 dark:bg-red-950/30",
  };
  return (
    <span className={`inline-flex items-center gap-1 rounded border px-1.5 py-0.5 text-[10px] font-medium uppercase tracking-wide ${styles[status] ?? ""}`}>
      {status === "Processing" && <Loader2 className="h-3 w-3 animate-spin" />}
      {status}
    </span>
  );
}

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

export default function KnowledgePage() {
  const [query, setQuery] = useState("");
  const [results, setResults] = useState<SearchResultDto[]>([]);
  const [searching, setSearching] = useState(false);
  const [searchError, setSearchError] = useState<string | null>(null);

  const [documents, setDocuments] = useState<KnowledgeDocumentDto[]>([]);

  // Контекст проекта: пустое значение = глобальная общая библиотека.
  const [projects, setProjects] = useState<ProjectDto[]>([]);
  const [projectId, setProjectId] = useState<string>("");

  // Общие поля формы.
  const [sourceType, setSourceType] = useState<string>(SOURCE_TYPES[1]);
  const [reference, setReference] = useState("");
  // Режим "вставить текст".
  const [title, setTitle] = useState("");
  const [content, setContent] = useState("");
  // Режим "загрузить файл".
  const [file, setFile] = useState<File | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const [uploading, setUploading] = useState(false);
  const [uploadError, setUploadError] = useState<string | null>(null);

  const loadDocuments = useCallback(() => {
    api.listDocuments(projectId || undefined).then(setDocuments).catch(() => {});
  }, [projectId]);

  useEffect(() => {
    api.listProjects().then(setProjects).catch(() => {});
  }, []);

  useEffect(() => {
    loadDocuments();
  }, [loadDocuments]);

  // Пока есть документы в очереди/обработке — опрашиваем статус каждые 2 секунды.
  const hasInProgress = documents.some((d) => d.status === "Pending" || d.status === "Processing");
  const pollRef = useRef<ReturnType<typeof setInterval> | null>(null);
  useEffect(() => {
    if (hasInProgress) {
      pollRef.current = setInterval(loadDocuments, 2000);
      return () => {
        if (pollRef.current) clearInterval(pollRef.current);
      };
    }
  }, [hasInProgress, loadDocuments]);

  function resetForm() {
    setTitle("");
    setReference("");
    setContent("");
    setFile(null);
    if (fileInputRef.current) fileInputRef.current.value = "";
  }

  async function upload(e: React.FormEvent) {
    e.preventDefault();
    setUploadError(null);

    try {
      if (file) {
        if (file.size > MAX_FILE_BYTES)
          throw new Error("File must be 10 MB or smaller.");
        setUploading(true);
        await api.uploadDocumentFile(file, {
          sourceType,
          reference: reference.trim() || undefined,
          projectId: projectId || undefined,
        });
      } else {
        if (!title.trim()) throw new Error("Title is required.");
        if (!content.trim()) throw new Error("Document content is required.");
        if (content.length > MAX_CONTENT)
          throw new Error(`Content is too long: ${content.length}/${MAX_CONTENT} characters.`);
        setUploading(true);
        await api.uploadDocument({
          title: title.trim(),
          sourceType,
          reference: reference.trim(),
          content,
          projectId: projectId || undefined,
        });
      }
      resetForm();
      loadDocuments();
    } catch (err) {
      setUploadError(err instanceof Error ? err.message : "Upload failed.");
    } finally {
      setUploading(false);
    }
  }

  async function search(e: React.FormEvent) {
    e.preventDefault();
    if (!query.trim()) return;
    setSearching(true);
    setSearchError(null);
    try {
      setResults(await api.searchKnowledge(query.trim(), projectId || undefined));
    } catch (err) {
      setResults([]);
      setSearchError(err instanceof Error ? err.message : "Search failed.");
    } finally {
      setSearching(false);
    }
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <h1 className="text-2xl font-bold">Knowledge Base</h1>
        <div>
          <label className="mr-2 text-xs text-muted-foreground" htmlFor="kb-scope">
            Scope
          </label>
          <select
            id="kb-scope"
            className="h-10 rounded-md border border-input bg-background px-3 text-sm"
            value={projectId}
            onChange={(e) => {
              setProjectId(e.target.value);
              setResults([]);
            }}
          >
            <option value="">Global library (shared)</option>
            {projects.map((p) => (
              <option key={p.id} value={p.id}>
                {p.name}
              </option>
            ))}
          </select>
        </div>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Add document</CardTitle>
        </CardHeader>
        <CardContent>
          <form onSubmit={upload} className="space-y-3">
            <div className="flex gap-2">
              <select
                className="h-10 rounded-md border border-input bg-background px-3 text-sm shrink-0"
                value={sourceType}
                onChange={(e) => setSourceType(e.target.value)}
              >
                {SOURCE_TYPES.map((t) => (
                  <option key={t} value={t}>{t}</option>
                ))}
              </select>
              <Input
                placeholder="Reference (DOI, patent number, experiment ID — optional)"
                value={reference}
                onChange={(e) => setReference(e.target.value)}
                maxLength={1000}
              />
            </div>

            {/* Выбор файла: PDF/DOCX/TXT/MD/CSV/XLS/XLSX, до 10 МБ */}
            <div className="flex items-center gap-2">
              <input
                ref={fileInputRef}
                type="file"
                className="hidden"
                accept={ACCEPTED_EXTENSIONS.join(",")}
                onChange={(e) => {
                  setFile(e.target.files?.[0] ?? null);
                  setUploadError(null);
                }}
              />
              {file ? (
                <div className="flex flex-1 items-center justify-between gap-2 rounded-md border border-blue-300 bg-blue-50 px-3 py-2 text-sm dark:border-blue-800 dark:bg-blue-950/30">
                  <span className="flex min-w-0 items-center gap-2">
                    <FileText className="h-4 w-4 shrink-0 text-blue-600" />
                    <span className="truncate">{file.name}</span>
                    <span className="shrink-0 text-xs text-muted-foreground">{formatBytes(file.size)}</span>
                  </span>
                  <button
                    type="button"
                    onClick={() => setFile(null)}
                    className="shrink-0 text-muted-foreground hover:text-foreground"
                    aria-label="Remove file"
                  >
                    <X className="h-4 w-4" />
                  </button>
                </div>
              ) : (
                <Button
                  type="button"
                  variant="outline"
                  className="flex-1"
                  onClick={() => fileInputRef.current?.click()}
                >
                  <Upload className="h-4 w-4" />
                  Choose file (PDF, DOCX, TXT, MD, CSV, XLS, XLSX · max 10 MB)
                </Button>
              )}
            </div>

            {!file && (
              <>
                <div className="flex items-center gap-2 text-xs text-muted-foreground">
                  <span className="h-px flex-1 bg-border" />
                  or paste text
                  <span className="h-px flex-1 bg-border" />
                </div>
                <Input
                  placeholder="Document title"
                  value={title}
                  onChange={(e) => setTitle(e.target.value)}
                  maxLength={300}
                />
                <textarea
                  className="flex min-h-[120px] w-full rounded-md border border-input bg-background px-3 py-2 text-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
                  placeholder={`Paste paper abstract, patent excerpt or internal notes… (max ${MAX_CONTENT} chars)`}
                  value={content}
                  onChange={(e) => setContent(e.target.value)}
                />
                <span className="block text-right text-xs text-muted-foreground">
                  {content.length}/{MAX_CONTENT}
                </span>
              </>
            )}

            <div className="flex justify-end">
              <Button type="submit" disabled={uploading}>
                {uploading ? <Loader2 className="h-4 w-4 animate-spin" /> : <Upload className="h-4 w-4" />}
                Index document
              </Button>
            </div>
            {uploadError && <p className="text-sm text-red-600">{uploadError}</p>}
          </form>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Semantic search</CardTitle>
        </CardHeader>
        <CardContent>
          <form onSubmit={search} className="flex gap-2">
            <Input placeholder="Search literature, tables and internal notes…" value={query} onChange={(e) => setQuery(e.target.value)} />
            <Button type="submit" disabled={searching}>
              {searching ? <Loader2 className="h-4 w-4 animate-spin" /> : <Search className="h-4 w-4" />}
              Search
            </Button>
          </form>
          {searchError && <p className="mt-2 text-sm text-red-600">{searchError}</p>}
        </CardContent>
      </Card>

      {results.length > 0 && (
        <Card>
          <CardHeader>
            <CardTitle>Results</CardTitle>
          </CardHeader>
          <CardContent className="space-y-3">
            {results.map((r) => (
              <div key={r.chunkId} className="border-t pt-3 first:border-0 first:pt-0">
                <div className="flex justify-between gap-2">
                  <span className="font-medium">{r.documentTitle}</span>
                  <span className="text-xs text-muted-foreground shrink-0">
                    sim {r.similarity.toFixed(3)}
                  </span>
                </div>
                {r.reference && <div className="text-xs text-muted-foreground">{r.reference} · {r.sourceType}</div>}
                <p className="text-sm text-muted-foreground mt-1 whitespace-pre-line">{r.content}</p>
              </div>
            ))}
          </CardContent>
        </Card>
      )}

      <Card>
        <CardHeader>
          <CardTitle>Indexed documents ({documents.length})</CardTitle>
        </CardHeader>
        <CardContent className="text-sm">
          {documents.length === 0 ? (
            <p className="text-muted-foreground">No documents indexed yet. Add one above to enable semantic search.</p>
          ) : (
            <ul className="divide-y">
              {documents.map((d) => (
                <li key={d.id} className="flex items-center justify-between gap-3 py-2">
                  <div className="min-w-0">
                    <div className="flex items-center gap-1.5 font-medium truncate">
                      <FileText className="h-3.5 w-3.5 shrink-0 text-muted-foreground" />
                      <span className="truncate">{d.title}</span>
                    </div>
                    <div className="text-xs text-muted-foreground">
                      {d.sourceType}{d.reference ? ` · ${d.reference}` : ""}
                    </div>
                  </div>
                  <StatusBadge status={d.status} />
                </li>
              ))}
            </ul>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
