import { ServiceDetailView } from "./ServiceDetailView";

export default async function ServiceDetailPage({
  params,
}: {
  params: Promise<{ serviceId: string }>;
}) {
  const { serviceId } = await params;
  return <ServiceDetailView serviceId={decodeURIComponent(serviceId)} />;
}
