# UniShare — Edge Add-ons / Chrome Web Store listing (en-US)

## Short description

Save links and offline copies directly to your private local UniShare library.

## Full description

Save your current tab to UniShare on Windows. Review its title, choose a collection and keep either
the link or an offline copy with rendered text and supported visible images. UniShare stays local;
there is no cloud account, advertising or browsing-history tracking. The extension reads the active
tab only after your action and communicates with the Windows app on `127.0.0.1`.

Install UniShare for Windows first, keep it running, then enter the local port and pairing key from
Options and tools → Connect phone. Your saved content belongs to your local library and can sync
directly with Android through your configured private network. Store versions receive binary
updates through the browser; a development ZIP is not a store installation.

## Single purpose and permissions

Single purpose: save the user-selected tab in their local UniShare library.

- `activeTab`: read the selected tab's URL/title after a user gesture.
- `scripting`: extract rendered text/images when offline saving is requested.
- `storage`: keep the connection settings and language locally.
- `http://127.0.0.1/*`: communicate with UniShare on this computer only.

No remote code, analytics, ads, sale of data or remote collection of browsing history. Native store
updates are provided by the browser, not by downloaded JavaScript or an extension ZIP installer.

## Store assets and owner fields

Use the supplied 128px icon, 300px logo, 440×280 promotional tile and 1280×800 screenshots of the
actual options UI in English and Spanish. Screenshots use a new unpacked QA profile, no real keys,
and correctly show the development channel; store delivery is not claimed. Provide the publisher's
real contact details and verified store account. Never substitute example IDs or credentials.
