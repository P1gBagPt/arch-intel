import { DependencyGraphView } from "@/components/graph/DependencyGraphView";

export default async function GraphNodePage({
  params,
}: {
  params: Promise<{ nodeId: string }>;
}) {
  const { nodeId } = await params;
  return (
    <div className="h-full">
      <DependencyGraphView scope={decodeURIComponent(nodeId)} />
    </div>
  );
}
