INSERT INTO "ChemicalCatalog"
    ("Id", "PubChemCid", "CanonicalName", "CasNumber", "Formula", "MolarMass", "CachedAtUtc", "Smiles")
VALUES
    ('00000000-0000-4000-8000-000000002244', 2244, 'Aspirin', '50-78-2', 'C9H8O4', 180.16,
     NOW(), 'CC(=O)Oc1ccccc1C(=O)O')
ON CONFLICT ("PubChemCid") DO NOTHING;
