// Mirrors ArchIntel.Api.Contracts.ApiEnvelope<T> (src/Api/ArchIntel.Api/Contracts/ApiEnvelope.cs)
export interface PageInfo {
  limit: number;
  totalCount: number;
  hasNextPage: boolean;
  nextCursor: string | null;
}

export interface ApiEnvelope<T> {
  data: T;
  page: PageInfo | null;
}
