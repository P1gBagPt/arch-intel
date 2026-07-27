// Mirrors DiagramRequestDto / DiagramResponseDto (src/Api/ArchIntel.Api/Contracts/DiagramDtos.cs) — POST /diagram
export interface DiagramRequest {
  scope?: string;
  depth?: number;
  kinds?: string[];
  format?: string;
}

export interface DiagramResponse {
  format: string;
  content: string;
}
