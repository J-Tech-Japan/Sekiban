import { getAccessToken } from "@/server/lib/auth-helpers";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

export async function GET(request: Request) {
  const accessToken = await getAccessToken();
  if (!accessToken) {
    console.info("[SSE][approvals] Missing access token.");
    return new Response("Unauthorized", { status: 401 });
  }

  console.info("[SSE][approvals] Proxy connect.");
  const apiUrl = `${process.env.API_BASE_URL}/api/stream/approvals`;
  const apiRes = await fetch(apiUrl, {
    headers: { Authorization: `Bearer ${accessToken}` },
    signal: request.signal,
    cache: "no-store",
  });

  if (!apiRes.ok || !apiRes.body) {
    console.warn("[SSE][approvals] Upstream stream unavailable.", apiRes.status);
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
