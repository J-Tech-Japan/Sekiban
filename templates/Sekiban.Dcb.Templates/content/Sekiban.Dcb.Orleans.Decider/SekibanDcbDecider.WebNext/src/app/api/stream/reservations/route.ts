import { proxyEventStream } from "@/server/lib/sse-proxy";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";

export async function GET(request: Request) {
  return proxyEventStream(request, "/api/stream/reservations", "reservations");
}
