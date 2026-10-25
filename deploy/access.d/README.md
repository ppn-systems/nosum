# access.d/

Extra Caddy rules for this deployment, mounted read-only into the proxy and imported inside the site block
(`*.caddyfile` only; nothing else here is read). Use it for what depends on the machine and must not be committed, for example basic auth in
front of the client while the deployment is private:

```caddyfile
@notTeam not remote_ip 203.0.113.0/24
basic_auth @notTeam {
	team <output of: docker run --rm caddy caddy hash-password>
}
```

Files ending in `.caddyfile` here are git-ignored.
