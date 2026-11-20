import { getAccessToken, refreshSessionTokens } from "@/server/lib/auth-helpers";

/**
 * Proxy a server-sent event stream from the API, authenticating with the session's access token.
 * When the API rejects an expired access token, the session is refreshed once and the request is
 * retried, so an open page keeps receiving events after the short-lived token expires.
 */
export async function proxyEventStream(request: Request, apiPath: string, label: string): Promise<Response> {
  const accessToken = await getAccessToken();
  if (!accessToken) {
    console.info(`[SSE][${label}] Missing access token.`);
    return new Response("Unauthorized", { status: 401 });
  }

  console.info(`[SSE][${label}] Proxy connect.`);
  const apiUrl = `${process.env.API_BASE_URL}${apiPath}`;
  const open = (token: string) =>
    fetch(apiUrl, {
      headers: { Authorization: `Bearer ${token}` },
      signal: request.signal,
      cache: "no-store",
    });

  let apiRes = await open(accessToken);
  if (apiRes.status === 401) {
    await apiRes.body?.cancel();
    const refreshedToken = await refreshSessionTokens();
    if (!refreshedToken) {
      console.info(`[SSE][${label}] Access token expired and could not be refreshed.`);
      return new Response("Unauthorized", { status: 401 });
    }
    apiRes = await open(refreshedToken);
  }

  if (!apiRes.ok || !apiRes.body) {
    console.warn(`[SSE][${label}] Upstream stream unavailable.`, apiRes.status);
    return new Response("Stream unavailable", { status: apiRes.status });
  }

  return new Response(apiRes.body, {
    headers: {
      "Content-Type": "text/event-stream",
      "Cache-Control": "no-cache",
      Connection: "keep-alive",
      "X-Accel-Buffering": "no",
    },
  });
}
