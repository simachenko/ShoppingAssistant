-- Provisions one database + one least-privileged role per bounded-context service, on the
-- single shared Postgres instance used for local/demo environments (research.md §5). Using a
-- separate physical database per service (not just a schema) means Postgres itself refuses
-- cross-service queries outright — there is no dblink/fdw configured, so "no cross-service
-- database queries" is enforced by the engine, not just convention.
--
-- Run automatically by the official postgres image on first container start (docker-compose
-- mounts this directory as /docker-entrypoint-initdb.d). For Neon, run this once by hand via
-- psql against the project's connection string (see quickstart.md).

-- Product Catalog Service
CREATE ROLE catalog_role LOGIN PASSWORD 'catalog_dev_password';
CREATE DATABASE catalogdb OWNER catalog_role;
REVOKE ALL ON DATABASE catalogdb FROM PUBLIC;
GRANT ALL PRIVILEGES ON DATABASE catalogdb TO catalog_role;

-- Pricing and Availability Service
CREATE ROLE pricing_role LOGIN PASSWORD 'pricing_dev_password';
CREATE DATABASE pricingdb OWNER pricing_role;
REVOKE ALL ON DATABASE pricingdb FROM PUBLIC;
GRANT ALL PRIVILEGES ON DATABASE pricingdb TO pricing_role;

-- Product Advisor Service (conversation history only — no product/price data)
CREATE ROLE advisor_role LOGIN PASSWORD 'advisor_dev_password';
CREATE DATABASE advisordb OWNER advisor_role;
REVOKE ALL ON DATABASE advisordb FROM PUBLIC;
GRANT ALL PRIVILEGES ON DATABASE advisordb TO advisor_role;
