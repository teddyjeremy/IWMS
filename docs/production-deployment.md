# IWMS production deployment checklist

This guide separates the Windows desktop package from the hosted API and Neon database. Publishing the desktop executable alone does not deploy the API.

## Architecture

- **Windows client:** WPF application distributed as a Windows x64 package.
- **API:** ASP.NET Core application hosted on a public HTTPS endpoint.
- **Database:** Existing Neon PostgreSQL project `IWMS`, production branch.
- **Identity:** Existing IWMS API email/password authentication and JWT authorization. Do not put a PostgreSQL connection string or JWT signing key in the desktop client.

The desktop client sends HTTPS requests to the API. The API reads and writes the Neon database using its server-side connection string.

## API host configuration

A portable Docker image is included at the repository root. Build it with `docker build -t iwms-api .` and deploy the image to a container host such as Azure Container Apps or another managed container service. The container listens on port `8080`; configure the host to provide public HTTPS, environment variables/secrets, outbound PostgreSQL connectivity, and health checks. Alternatively, publish the API directly to a managed ASP.NET Core host.

Set these values in the API host's secret/configuration settings:

- `ConnectionStrings__DefaultConnection`: the Neon connection string for the production database. Prefer Neon's pooled connection for normal application traffic.
- `Jwt__Key`: a cryptographically random signing key with at least 32 random bytes.
- `Jwt__Issuer`: `IWMS` unless the API configuration is deliberately changed.
- `Jwt__Audience`: `IWMS` unless the API configuration is deliberately changed.
- `ASPNETCORE_ENVIRONMENT`: `Production`.
- `BootstrapAdmin__Email`: the first real administrator's email, for initial provisioning only.
- `BootstrapAdmin__Password`: a unique strong password for that account (10+ characters, letters and digits).
- `BootstrapAdmin__Name`: optional display name for the first administrator.

Never commit these values to Git or package them with the desktop app. Use the direct Neon connection only for EF migrations; use the pooled connection for normal API requests.

### Database migrations

Review the existing EF Core migrations and run the migration command as a controlled deployment step before sending traffic to the new API version. Do not rely on Development-only startup migration/seeding for production. Take a Neon snapshot before production schema changes.

### User provisioning

The current API supports sign-in for existing active users. It does not expose public self-registration, which is intentional for an inventory system. If no real administrator exists, the optional `BootstrapAdmin__Email`, `BootstrapAdmin__Password`, and `BootstrapAdmin__Name` host settings create one administrator account on API startup. Existing accounts are never overwritten; remove the bootstrap secrets after confirming sign-in. Then use the administrator-only **Users & access** desktop page to create named users with appropriate roles, reset passwords, and deactivate development accounts. Do not distribute shared demo credentials or the development seed password. The development seeder uses local demo email addresses and should not be treated as production account provisioning. The connected Neon database currently contains six `@iwms.local` development-seed accounts; review and deactivate them through the admin UI after creating and verifying a real administrator.

## Configure the Windows client

The sign-in screen accepts the API base URL. For local development, `https://localhost:7001/` is the default. A production deployment must use the real HTTPS API URL.

For managed installations, the client also reads `IWMS_API_BASE_URL` from its process environment as the initial URL. This value is a public endpoint, not a secret. Remote HTTP URLs are rejected; HTTP is permitted only for localhost development.

Verify with a production-like test account:
1. Invalid credentials are rejected.
2. A valid account can sign in and load the dashboard.
3. The sidebar is hidden before authentication and after sign-out.
4. Protected API endpoints reject requests without a valid bearer token.
5. Data created by one authorized client is visible to another authorized client connected to the same API.
6. The deployed API health endpoint responds at `/health`.

## Microsoft Store release

The repository's current GitHub Actions workflow builds and publishes self-contained Windows x64 API and desktop artifacts. These are build artifacts, not a Store submission.

Before Store submission:
1. Reserve the app name in Microsoft Partner Center.
2. Use the exact package identity and publisher values supplied by Partner Center in the MSIX manifest.
3. Set app display name, version, icons, tile assets, publisher details, privacy policy and support information.
4. Package and validate the MSIX, then test installation, upgrade and uninstall on a clean Windows machine.
5. Upload the package to Partner Center, complete Store listing and certification requirements, and publish when approved.
6. Verify that the installed client points to the deployed HTTPS API—not localhost.

A Store package cannot be finalized correctly until the Partner Center package identity and the production API URL are known.
