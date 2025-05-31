# GitHub.Release.Proxy
This API proxies requests to GitHub release assets. This is useful as some systems do not allow downloads to be redirected to another URL.

Microsoft Store, `The package URL redirects to another URL. Provide a download URL without redirection.`

Paths:
- /releases/download/{{version}}/{{assetName}}
- /openapi/v1.json