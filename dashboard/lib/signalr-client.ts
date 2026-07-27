import * as signalR from "@microsoft/signalr";

// /hubs/architecture is unversioned, unlike the REST surface (Program.cs) — SignalR hub paths
// aren't part of the API version being tracked.
const DEFAULT_SIGNALR_URL = "http://localhost:5219/hubs/architecture";

export function getSignalRUrl() {
  return process.env.NEXT_PUBLIC_SIGNALR_URL ?? DEFAULT_SIGNALR_URL;
}

export function createArchitectureHubConnection(url: string = getSignalRUrl()) {
  return new signalR.HubConnectionBuilder()
    // @microsoft/signalr defaults withCredentials to true, which sends fetch credentials:
    // "include" and then requires the server to answer with Access-Control-Allow-Credentials.
    // The API's CORS policy (Program.cs) doesn't set that — DevBearer auth is a header, not a
    // cookie, so there's nothing to send credentials for — which without this override fails
    // every negotiate/transport request as an opaque net::ERR_FAILED.
    .withUrl(url, { withCredentials: false })
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .configureLogging(signalR.LogLevel.Warning)
    .build();
}
