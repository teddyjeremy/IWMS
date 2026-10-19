# IWMS production deployment checklist

This guide separates the Windows desktop package from the hosted API and Neon database. Publishing the desktop executable alone does not deploy the API.

## Architecture

- **Windows client:** WPF application distributed as a Windows x64 package.
- **API:** ASP.NET Core application hosted on a public HTTPS endpoint.
- **Database:** Existing Neon PostgreSQL project `IWMS`, production branch.
- **Identity:** Existing IWMS API email/password authentication and JWT authorization. Do not put a PostgreSQL connection string or JWT signing key in the desktop client.

The desktop client sends HTTPS requests to the API. The API reads and writes the Neon database using its server-side connection string.

## Recommended first host: Render

A Render Blueprint file (`render.yaml`) is included at the repository root. It defines a Docker web service named `iwms-api`, uses the repository's `Dockerfile`, selects Render's Ohio region, configures `/health` as the health check, generates a JWT signing key, and prompts for the database and initial-admin secrets. The starter plan is a paid always-on service; review Render's current pricing before creating it.

1. Push the latest `master` branch to GitHub.
2. Sign in to Render and choose **New → Blueprint**. Connect `teddyjeremy/IWMS` and select the branch containing `render.yaml`.
3. Review the service settings. Keep the region as **Ohio** to match the current Neon project region (`aws-us-east-2`) and reduce database network latency.
4. During setup, provide the values marked as unsynchronized secrets:
   - `ConnectionStrings__DefaultConnection`: copy the connection string for the existing Neon **production** branch and database from Neon. It must include SSL (normally `sslmode=require`). Never put it in Git or in the desktop app.
   - `BootstrapAdmin__Email`: the email for the first real administrator.
   - `BootstrapAdmin__Password`: a unique strong password, at least 10 characters, containing letters and digits.
   - `BootstrapAdmin__Name`: the administrator's display name.
5. Keep the generated `Jwt__Key` private. The blueprint sets `Jwt__Issuer=IWMS.Api` and `Jwt__Audience=IWMS.Client`, matching the API's checked-in configuration.
6. Create the service and wait for the deployment and `/health` check to pass. Copy the HTTPS service URL assigned by Render.
7. Sign in to IWMS using that URL and the new administrator account. After confirming sign-in works, remove `BootstrapAdmin__Email`, `BootstrapAdmin__Password`, and `BootstrapAdmin__Name` from the Render environment settings and redeploy if prompted.
8. Use **Users & access** to create named users with only the roles they need. Review and deactivate the six existing `@iwms.local` development-seed accounts after verifying the real administrator.

The Render blueprint does not create a new Neon database, run EF migrations, or modify existing inventory data. Before routing production users to the API, review migrations and apply any required migration as a controlled deployment step. Take a Neon snapshot before schema changes. The current production branch already has the IWMS role records required by initial administrator provisioning.

### Database connection guidance

Use the connection string from the correct Neon project, branch, and database. The API host must be able to make outbound TLS connections to Neon. A direct connection is acceptable for the first low-volume deployment; use Neon's pooled endpoint when connection pooling is enabled and you have copied that endpoint's full connection string. Do not assume a pooler is enabled merely because a pooled hostname is shown in the console.

## Alternative host: Azure Container Apps

If you later choose Azure, the same Docker image can be deployed to Azure Container Apps. You will need an Azure subscription, a container image source (for example, Azure Container Registry), HTTPS ingress, environment secrets, and a deployment identity. Those cloud resources and credentials are intentionally not created by this repository's Render Blueprint.

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
