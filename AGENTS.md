# Local app startup

- Start the web app from a process with ordinary, approved network access when external FHIR sources are configured. In a network-restricted tool session, request escalation for the launch rather than leaving a background server with inherited blocked proxy settings.
- Never remove proxy variables, disable TLS checks, or bypass a network restriction to make a source reachable. The app rejects a known loopback discard proxy on port 9 at startup.
- Local self-tests and synthetic HTTP/browser tests must run before checks against external test endpoints. No Fabric refreshes or production operations are part of app startup.
- Verify `/health` after startup. Use the source status check for connectivity; it makes one anonymous HEAD request and never reads patient data. A successful health check for the local app alone does not verify an external FHIR source.
