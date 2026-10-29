export type ThemeColor = 'cyan' | 'emerald' | 'purple' | 'amber' | 'blue';

export interface ProjectInfo {
  id: string;
  tag: string;
  name: string;
  folderPath: string;
  description: string;
  videoCount: number;
  totalDuration: string;
  assetsCount: number;
  lastModified: string;
  coverImageUrl: string;
}

export interface ProjectFolder {
  id: string;
  name: string;
  subtitle: string;
  detail: string;
  theme: 'cyan' | 'emerald' | 'purple' | 'amber';
  iconType: 'folder' | 'assets' | 'work' | 'delivery';
  path?: string;
}

export type StepState = 'completed' | 'in_progress' | 'pending';

export interface PipelineStep {
  id: string;
  number: number;
  title: string;
  status: StepState;
  statusLabel: string;
}

export interface PipelineProgress {
  currentTask: string;
  progressPercentage: number;
  currentItem: string;
  estimatedTime: string;
  steps: PipelineStep[];
}

export interface MediaAnalysisItem {
  id: string;
  filename: string;
  duration: string;
  resolution: string;
  device: string;
  category: string;
  categoryLabel: string;
  categoryTheme: 'emerald' | 'purple' | 'amber' | 'blue';
  thumbnailUrl: string;
  status: 'completed' | 'processing' | 'pending';
}

export interface QuickAction {
  id: string;
  label: string;
  variant: 'primary' | 'secondary';
  iconType: 'play' | 'continue' | 'davinci' | 'report';
  onClick?: () => void;
}

export interface ToolIntegration {
  id: string;
  name: string;
  status: 'connected' | 'available' | 'offline';
  statusLabel: string;
  iconType: 'davinci' | 'whisper' | 'ffmpeg' | 'hyperframes' | 'claude';
}

export interface RecentActivityItem {
  id: string;
  title: string;
  description: string;
  timestamp: string;
  type: 'transcription' | 'inventory' | 'connection' | 'project';
}

export interface HomeDashboardData {
  project: ProjectInfo;
  folders: ProjectFolder[];
  pipeline: PipelineProgress;
  recentAnalyses: MediaAnalysisItem[];
  quickActions: QuickAction[];
  tools: ToolIntegration[];
  activities: RecentActivityItem[];
}
