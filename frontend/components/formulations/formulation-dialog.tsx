"use client";

import { useState } from "react";
import { api, type FormulationDto } from "@/lib/api";
import { Modal } from "@/components/ui/modal";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";

export function FormulationDialog({
  projectId,
  open,
  onClose,
  onCreated,
}: {
  projectId: string;
  open: boolean;
  onClose: () => void;
  onCreated: (formulation: FormulationDto) => void;
}) {
  const [name, setName] = useState("");
  const [targetPurpose, setTargetPurpose] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setSaving(true);
    try {
      const f = await api.createFormulation({
        projectId,
        name: name.trim(),
        targetPurpose: targetPurpose.trim(),
      });
      setName("");
      setTargetPurpose("");
      onCreated(f);
      onClose();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to create formulation");
    } finally {
      setSaving(false);
    }
  }

  return (
    <Modal open={open} onClose={onClose} title="New formulation">
      <form onSubmit={submit} className="space-y-4">
        <Input
          placeholder="Name"
          value={name}
          onChange={(e) => setName(e.target.value)}
          required
          autoFocus
        />
        <Input
          placeholder="Target purpose"
          value={targetPurpose}
          onChange={(e) => setTargetPurpose(e.target.value)}
          required
        />
        {error && <p className="text-sm text-destructive">{error}</p>}
        <div className="flex gap-2">
          <Button type="submit" disabled={saving || !name.trim() || !targetPurpose.trim()}>
            Create
          </Button>
          <Button type="button" variant="outline" onClick={onClose}>
            Cancel
          </Button>
        </div>
      </form>
    </Modal>
  );
}
