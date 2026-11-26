# Security Policy

Lavagon is a **local development** environment. Its services are meant for your own computer, not for production or public servers.

## Supported versions

Only the latest release receives fixes.

## Reporting a vulnerability

Please **do not** open a public issue for security problems. Use GitHub's private reporting instead:
[Report a vulnerability](https://github.com/funwkwk/lavagon/security/advisories/new).

Include the Lavagon version, the steps to reproduce and the impact. You will get an answer as soon as possible.

## Good to know

- Apache and Nginx listen on all network interfaces (so you can test from a phone on your LAN). Use Windows Firewall to block them on untrusted networks.
- MySQL starts with an empty `root` password; set one in *Menu > MySQL > Change root password*. Lavagon stores it encrypted with Windows DPAPI.
- The *Lavagon Local CA* private key is stored in `etc\ssl\`. Anyone who can read it can create certificates your browser trusts if you added the CA to your trust store — keep the folder private.
