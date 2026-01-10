import { getAccessToken } from "@/server/lib/auth-helpers";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

export async function GET(request: Request) {
  const accessToken = await getAccessToken();
  if (!accessToken) {
    console.info("[SSE][reservations] Missing access token.");
    return new Response("Unauthorized", { status: 401 });
  }

  console.info("[SSE][reservations] Proxy connect.");
  const apiUrl = `${process.env.API_BASE_URL}/api/stream/reservations`;
  const apiRes = await fetch(apiUrl, {
    headers: { Authorization: `Bearer ${accessToken}` },
    signal: request.signal,
    cache: "no-store",
  });

  if (!apiRes.ok || !apiRes.body) {
    console.warn("[SSE][reservations] Upstream stream unavailable.", apiRes.status);
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
