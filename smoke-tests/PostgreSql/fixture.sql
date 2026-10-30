-- Dedicated test schema only. The automated runner replaces this quoted schema
-- with its own random, safely quoted identifier and drops only that schema.
CREATE SCHEMA "Designer.Phase2";
CREATE TABLE "Designer.Phase2"."Customers" (
    "Id" integer PRIMARY KEY,
    "Name" text NOT NULL,
    "Email" text NULL,
    "City" text NULL
);
CREATE TABLE "Designer.Phase2"."Orders" (
    "Id" integer PRIMARY KEY,
    "CustomerId" integer NOT NULL REFERENCES "Designer.Phase2"."Customers" ("Id"),
    "OrderDate" timestamp without time zone NOT NULL,
    "Amount" numeric(12,2) NULL
);
CREATE TABLE "Designer.Phase2"."EmptyTable" ("Id" integer PRIMARY KEY, "Name" text NULL);
CREATE TABLE "Designer.Phase2"."Order.Items" (
    "Id" integer PRIMARY KEY, "name" text NOT NULL, "Name" text NULL, "display.name" text NULL
);
INSERT INTO "Designer.Phase2"."Customers" VALUES
    (101, 'Alice North', 'alice@example.test', 'Kazan'),
    (205, 'Boris River', NULL, 'Perm'),
    (309, 'Clara Pine', 'clara@example.test', NULL);
INSERT INTO "Designer.Phase2"."Orders" VALUES
    (1001, 101, TIMESTAMP '2026-01-10 09:30:00', 1250.50),
    (1002, 205, TIMESTAMP '2026-02-15 18:10:00', 42.75),
    (1003, 101, TIMESTAMP '2026-03-20 12:00:00', NULL);
INSERT INTO "Designer.Phase2"."Order.Items" VALUES (1, 'lowercase', 'Uppercase', 'dot value');
CREATE VIEW "Designer.Phase2"."CustomersView" AS SELECT * FROM "Designer.Phase2"."Customers";
