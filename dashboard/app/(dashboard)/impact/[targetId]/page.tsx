import { ImpactResultView } from "./ImpactResultView";

export default async function ImpactResultPage({
  params,
}: {
  params: Promise<{ targetId: string }>;
}) {
  const { targetId } = await params;
  return <ImpactResultView targetId={decodeURIComponent(targetId)} />;
}
