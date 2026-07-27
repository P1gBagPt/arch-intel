import { Handle, Position, type NodeProps } from "reactflow";
import { Badge } from "@/components/ui/Badge";

export interface MiniGraphNodeData {
  label: string;
  kind: string;
  isCenter?: boolean;
}

export function MiniGraphNode({ data }: NodeProps<MiniGraphNodeData>) {
  return (
    <div
      className={`rounded-md border px-3 py-2 text-xs shadow-sm ${
        data.isCenter ? "border-accent bg-accent/10 font-semibold" : "border-surface-border bg-background"
      }`}
    >
      <Handle type="target" position={Position.Left} className="!bg-muted-foreground" />
      <div className="flex flex-col gap-1">
        <span className="max-w-[140px] truncate">{data.label}</span>
        <Badge className="w-fit">{data.kind}</Badge>
      </div>
      <Handle type="source" position={Position.Right} className="!bg-muted-foreground" />
    </div>
  );
}
