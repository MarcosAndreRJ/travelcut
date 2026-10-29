import React from 'react';
import { clsx } from 'clsx';
import { twMerge } from 'tailwind-merge';

export interface ProgressBarProps extends React.HTMLAttributes<HTMLDivElement> {
  value: number; // 0 to 100
  max?: number;
  variant?: 'primary' | 'turquoise' | 'success' | 'warning' | 'error';
  size?: 'xs' | 'sm' | 'md';
  showLabel?: boolean;
  label?: string;
  animated?: boolean;
}

const variantStyles = {
  primary: 'bg-studio-primary shadow-glow-blue',
  turquoise: 'bg-studio-turquoise shadow-glow-cyan',
  success: 'bg-studio-success shadow-glow-emerald',
  warning: 'bg-studio-warning shadow-glow-amber',
  error: 'bg-studio-error'
};

const sizeStyles = {
  xs: 'h-1.5',
  sm: 'h-2',
  md: 'h-3'
};

export const ProgressBar: React.FC<ProgressBarProps> = ({
  value,
  max = 100,
  variant = 'primary',
  size = 'sm',
  showLabel = false,
  label,
  animated = false,
  className,
  ...props
}) => {
  const percentage = Math.min(100, Math.max(0, (value / max) * 100));

  return (
    <div className={twMerge(clsx('w-full flex flex-col gap-1.5', className))} {...props}>
      {(showLabel || label) && (
        <div className="flex items-center justify-between text-xs text-studio-muted">
          {label && <span className="font-medium text-studio-text">{label}</span>}
          {showLabel && <span className="font-mono text-studio-subtle">{Math.round(percentage)}%</span>}
        </div>
      )}

      <div
        className={twMerge(
          clsx(
            'w-full bg-studio-card border border-studio-border/70 rounded-full overflow-hidden relative',
            sizeStyles[size]
          )
        )}
        role="progressbar"
        aria-valuenow={percentage}
        aria-valuemin={0}
        aria-valuemax={100}
      >
        <div
          className={twMerge(
            clsx(
              'h-full rounded-full transition-all duration-300 ease-out',
              variantStyles[variant],
              animated && 'animate-pulse'
            )
          )}
          style={{ width: `${percentage}%` }}
        />
      </div>
    </div>
  );
};
