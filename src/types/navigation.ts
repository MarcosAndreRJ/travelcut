import React from 'react';

export type NavigationTab =
  | 'home'
  | 'project'
  | 'analysis'
  | 'edit'
  | 'review'
  | 'export'
  | 'settings';

export interface NavigationItem {
  id: NavigationTab;
  label: string;
  icon: React.ComponentType<{ className?: string }>;
  badge?: string | number;
  badgeVariant?: 'primary' | 'success' | 'warning' | 'info';
  description?: string;
  shortcut?: string;
}
