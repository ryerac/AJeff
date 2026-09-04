# Pi-hole Monitor

Adds a dynamic deck button for each Pi-hole you want to watch. The button shows
whether the Pi-hole v6 API is reachable and the blocked-query count reported by
its 24-hour summary. Pressing the button refreshes it immediately.

After applying the **Pi-hole Monitor** preset, configure its action parameters:

- **Pi-hole URL**: the server base URL, such as `http://pi.hole` or
  `https://192.168.1.2`. URLs ending in `/admin` or `/api` are also accepted.
- **Display name**: a short name for this instance.
- **Application password**: preferably a dedicated Pi-hole v6 application
  password. Leave it empty only if API authentication is disabled.

Refresh interval, request timeout, and support for trusted self-signed local
certificates are available in JeffDock's plugin settings.

The application password is currently saved with the button binding as plain
text, so use a dedicated Pi-hole application password rather than the web
interface password.
