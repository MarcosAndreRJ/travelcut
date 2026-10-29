import React from 'react';
import { clsx } from 'clsx';
import { twMerge } from 'tailwind-merge';

export interface EmptyStateProps extends React.HTMLAttributes<HTMLDivElement> {
  icon?: React.ComponentType<{ className?: string }>;
  title: string;
  description: string;
  action?: React.ReactNode;
}

export const EmptyState: React.FC<EmptyStateProps> = ({
  icon: Icon,
  title,
  description,
  action,
  className,
  children,
  ...props
}) => {
  return (
    <div
      className={twMerge(
        clsx(
          'flex flex-col items-center justify-center text-center p-8 border border-dashed border-studio-border rounded-2xl bg-studio-card/30 max-w-md mx-auto my-6',
          className
        )
      )}
      {...props}
    >
      {Icon && (
        <div className="w-12 h-12 rounded-xl bg-studio-panel border border-studio-border flex items-center justify-center text-studio-primary mb-3 shadow-panel">
          <Icon className="w-6 h-6" />
        </div>
      )}

      <h4 className="text-sm font-bold text-white mb-1.5">{title}</h4>
      <p className="text-xs text-studio-muted leading-relaxed max-w-sm mb-4">{description}</p>

      {action && <div className="mt-1">{action}</div>}
      {children}
    </div>
  );
};
