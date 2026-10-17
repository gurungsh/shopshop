-- Mock users for the outdoor and recreation shop. Safe to rerun: existing rows are left alone.
-- Passwords: admin@example.com / Admin123!, every customer / Customer123!
-- The hashes are ASP.NET Core PasswordHasher (V3) output, the same format Identity writes on register.
BEGIN;

INSERT INTO "Users" ("Id", "Email", "PasswordHash", "Role", "CreatedAtUtc", "UpdatedAtUtc") VALUES
('0a000000-0000-0000-0000-000000000001', 'admin@example.com',          'AQAAAAIAAYagAAAAECdCIoutf1//QgfUMO2NKBwsZpocboSMTyLuIAlN8FL4A/IasNcM1Bqt10lnWpx2hA==', 'Admin',    '2026-06-01T09:00:00Z', '2026-06-01T09:00:00Z'),
('0a000000-0000-0000-0000-000000000002', 'emily.chen@example.com',     'AQAAAAIAAYagAAAAEP2ZlKa53FbqoSgIEWPBhyKNMYyVIG2KirAhqTerRz2sXK1LLAlvcKcqNm6kjL/iJQ==', 'Customer', '2026-06-20T14:12:00Z', '2026-06-20T14:12:00Z'),
('0a000000-0000-0000-0000-000000000003', 'marcus.johnson@example.com', 'AQAAAAIAAYagAAAAEP2ZlKa53FbqoSgIEWPBhyKNMYyVIG2KirAhqTerRz2sXK1LLAlvcKcqNm6kjL/iJQ==', 'Customer', '2026-07-02T18:40:00Z', '2026-07-02T18:40:00Z'),
('0a000000-0000-0000-0000-000000000004', 'sofia.garcia@example.com',   'AQAAAAIAAYagAAAAEP2ZlKa53FbqoSgIEWPBhyKNMYyVIG2KirAhqTerRz2sXK1LLAlvcKcqNm6kjL/iJQ==', 'Customer', '2026-07-15T11:05:00Z', '2026-07-15T11:05:00Z'),
('0a000000-0000-0000-0000-000000000005', 'liam.oconnor@example.com',   'AQAAAAIAAYagAAAAEP2ZlKa53FbqoSgIEWPBhyKNMYyVIG2KirAhqTerRz2sXK1LLAlvcKcqNm6kjL/iJQ==', 'Customer', '2026-07-28T08:30:00Z', '2026-07-28T08:30:00Z'),
('0a000000-0000-0000-0000-000000000006', 'priya.patel@example.com',    'AQAAAAIAAYagAAAAEP2ZlKa53FbqoSgIEWPBhyKNMYyVIG2KirAhqTerRz2sXK1LLAlvcKcqNm6kjL/iJQ==', 'Customer', '2026-08-05T20:15:00Z', '2026-08-05T20:15:00Z')
ON CONFLICT DO NOTHING;

COMMIT;
