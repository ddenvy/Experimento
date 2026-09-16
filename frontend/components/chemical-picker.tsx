"use client";

import { useEffect, useRef, useState } from "react";
import { api, type ChemicalDto, type ChemicalSuggestion } from "@/lib/api";
import { Input } from "@/components/ui/input";
import { Loader2, CheckCircle2, X, Search } from "lucide-react";

interface ChemicalPickerProps {
  value: ChemicalDto | null;
  onSelect: (chemical: ChemicalDto) => void;
  onClear: () => void;
  placeholder?: string;
}

type SuggestStatus = "idle" | "loading" | "ready" | "error";

// Подпись типа совпадения для бейджа в списке кандидатов.
const MATCH_LABEL: Record<ChemicalSuggestion["matchType"], string> = {
  local: "Catalog",
  name: "Name",
  formula: "Formula",
  cas: "CAS",
};

/**
 * Поле выбора вещества из каталога PubChem.
 * Одно поле понимает название/синоним, CAS-номер (50-78-2) и молекулярную
 * формулу (C9H8O4) — по формуле возвращаются изомеры, пользователь выбирает
 * конкретное вещество. Свободный ввод недоступен: CID/формула/масса приходят с сервера.
 */
export function ChemicalPicker({ value, onSelect, onClear, placeholder = "Search by name, formula or CAS…" }: ChemicalPickerProps) {
  const [query, setQuery] = useState("");
  const [suggestions, setSuggestions] = useState<ChemicalSuggestion[]>([]);
  const [open, setOpen] = useState(false);
  const [status, setStatus] = useState<SuggestStatus>("idle");
  const [resolving, setResolving] = useState<string | null>(null);
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Автоподсказки с задержкой 300 мс планируются из обработчика ввода
  // (реакция на событие, а не на изменение состояния) — без setState в эффекте.
  function scheduleSuggestions(raw: string) {
    if (debounceRef.current) clearTimeout(debounceRef.current);
    const q = raw.trim();
    if (q.length < 2) {
      setSuggestions([]);
      setStatus("idle");
      return;
    }
    setStatus("loading");
    debounceRef.current = setTimeout(() => {
      api
        .suggestChemicals(q)
        .then((items) => {
          setSuggestions(items);
          setStatus(items.length > 0 ? "ready" : "error");
        })
        .catch(() => {
          setSuggestions([]);
          setStatus("error");
        });
    }, 300);
  }

  // Снимаем отложенный запрос при размонтировании.
  useEffect(() => {
    return () => {
      if (debounceRef.current) clearTimeout(debounceRef.current);
    };
  }, []);

  async function pick(suggestion: ChemicalSuggestion) {
    setResolving(suggestion.name);
    try {
      // Кандидаты с CID (формула/CAS/локальный каталог) резолвятся детерминированно,
      // варианты названий — по имени (PUG сам выберет каноническую запись).
      const chemical = suggestion.pubChemCid !== null
        ? await api.resolveChemicalByCid(suggestion.pubChemCid)
        : await api.resolveChemical(suggestion.name);
      onSelect(chemical);
      setOpen(false);
    } catch {
      setStatus("error");
    } finally {
      setResolving(null);
    }
  }

  // Выбранное вещество: плашка с эталонными свойствами и кнопкой смены.
  if (value) {
    return (
      <div className="flex items-center justify-between gap-2 rounded-md border border-green-300 bg-green-50 px-3 py-2 text-sm dark:border-green-800 dark:bg-green-950/30">
        <div className="min-w-0">
          <div className="flex items-center gap-1.5 font-medium truncate">
            <CheckCircle2 className="h-3.5 w-3.5 text-green-600 shrink-0" />
            <span className="truncate">{value.name}</span>
          </div>
          <div className="text-xs text-muted-foreground mt-0.5">
            {value.formula ?? "—"} · {value.molarMass.toFixed(2)} g/mol
            {value.casNumber ? ` · CAS ${value.casNumber}` : ""} · CID {value.pubChemCid}
          </div>
        </div>
        <button
          type="button"
          onClick={() => {
            setQuery("");
            setSuggestions([]);
            setStatus("idle");
            onClear();
          }}
          className="text-muted-foreground hover:text-foreground shrink-0"
          aria-label="Change chemical"
        >
          <X className="h-4 w-4" />
        </button>
      </div>
    );
  }

  return (
    <div className="relative">
      <div className="relative">
        <Search className="absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
        <Input
          className="pl-8"
          value={query}
          placeholder={placeholder}
          autoComplete="off"
          onChange={(e) => {
            setQuery(e.target.value);
            setOpen(true);
            scheduleSuggestions(e.target.value);
          }}
          onFocus={() => setOpen(true)}
          // Задержка, чтобы клик по пункту списка успел отработать до закрытия.
          onBlur={() => setTimeout(() => setOpen(false), 150)}
        />
      </div>

      {open && query.trim().length >= 2 && (
        <div role="listbox" className="absolute z-20 mt-1 w-full rounded-md border bg-popover shadow-md max-h-60 overflow-y-auto">
          {status === "loading" && (
            <div className="flex items-center gap-2 px-3 py-2 text-sm text-muted-foreground">
              <Loader2 className="h-3.5 w-3.5 animate-spin" /> Searching…
            </div>
          )}
          {status === "error" && suggestions.length === 0 && resolving === null && (
            <div className="px-3 py-2 text-sm text-muted-foreground">
              No substances found — try a name, formula (C9H8O4) or CAS (50-78-2)
            </div>
          )}
          {suggestions.map((s) => (
            <button
              key={`${s.matchType}-${s.pubChemCid ?? s.name}`}
              type="button"
              role="option"
              aria-selected={false}
              onMouseDown={(e) => e.preventDefault()}
              onClick={() => void pick(s)}
              className="flex w-full items-center justify-between gap-2 px-3 py-2 text-left text-sm hover:bg-accent"
            >
              <span className="truncate">{s.name}</span>
              <span className="flex items-center gap-1.5 shrink-0 text-xs text-muted-foreground">
                {s.formula && <span className="font-mono">{s.formula}</span>}
                <span className="rounded border px-1 py-0.5 text-[10px] uppercase tracking-wide">
                  {MATCH_LABEL[s.matchType]}
                </span>
                {resolving === s.name && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
              </span>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
