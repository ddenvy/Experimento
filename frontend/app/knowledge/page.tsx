"use client";

import { useEffect, useState } from "react";
import { api, type KnowledgeDocumentDto, type SearchResultDto } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Search } from "lucide-react";

export default function KnowledgePage() {
  const [query, setQuery] = useState("");
  const [results, setResults] = useState<SearchResultDto[]>([]);
  const [documents, setDocuments] = useState<KnowledgeDocumentDto[]>([]);

  useEffect(() => {
    api.listDocuments().then(setDocuments).catch(() => {});
  }, []);

  async function search(e: React.FormEvent) {
    e.preventDefault();
    if (!query) return;
    try {
      const r = await api.searchKnowledge(query);
      setResults(r);
    } catch {
      setResults([]);
    }
  }

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-bold">Knowledge Base</h1>

      <Card>
        <CardHeader>
          <CardTitle>Semantic search</CardTitle>
        </CardHeader>
        <CardContent>
          <form onSubmit={search} className="flex gap-2">
            <Input placeholder="Search literature and internal notes…" value={query} onChange={(e) => setQuery(e.target.value)} />
            <Button type="submit">
              <Search className="h-4 w-4" /> Search
            </Button>
          </form>
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
                <div className="flex justify-between">
                  <span className="font-medium">{r.documentTitle}</span>
                  <span className="text-xs text-muted-foreground">
                    sim {r.similarity.toFixed(3)}
                  </span>
                </div>
                <p className="text-sm text-muted-foreground mt-1">{r.content}</p>
              </div>
            ))}
          </CardContent>
        </Card>
      )}

      <Card>
        <CardHeader>
          <CardTitle>Indexed documents ({documents.length})</CardTitle>
        </CardHeader>
        <CardContent className="text-sm text-muted-foreground">
          {documents.length === 0 ? "No documents indexed yet." : "Documents are ready for semantic search."}
        </CardContent>
      </Card>
    </div>
  );
}
