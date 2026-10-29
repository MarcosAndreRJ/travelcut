import React, { forwardRef } from 'react';
import { clsx } from 'clsx';
import { twMerge } from 'tailwind-merge';

export interface CardProps extends React.HTMLAttributes<HTMLDivElement> {
  variant?: 'default' | 'elevated' | 'interactive' | 'outline';
  density?: 'compact' | 'normal' | 'spacious';
}

export const Card = forwardRef<HTMLDivElement, CardProps>(
  ({ className, variant = 'default', density = 'normal', children, ...props }, ref) => {
    const variantStyles = {
      default: 'bg-studio-card border border-studio-border',
      elevated: 'bg-studio-card border border-studio-border shadow-panel',
      interactive:
        'bg-studio-card border border-studio-border hover:border-studio-borderLight hover:bg-studio-cardHover transition-all cursor-pointer',
      outline: 'bg-transparent border border-studio-border/70'
    };

    const densityStyles = {
      compact: 'p-3 rounded-lg',
      normal: 'p-4 rounded-xl',
      spacious: 'p-6 rounded-2xl'
    };

    return (
      <div
        ref={ref}
        className={twMerge(
          clsx(
            'text-studio-text relative overflow-hidden transition-colors',
            variantStyles[variant],
            densityStyles[density],
            className
          )
        )}
        {...props}
      >
        {children}
      </div>
    );
  }
);

Card.displayName = 'Card';

export const CardHeader = forwardRef<HTMLDivElement, React.HTMLAttributes<HTMLDivElement>>(
  ({ className, children, ...props }, ref) => (
    <div
      ref={ref}
      className={twMerge(clsx('flex items-center justify-between gap-3 mb-3 shrink-0', className))}
      {...props}
    >
      {children}
    </div>
  )
);
CardHeader.displayName = 'CardHeader';

export const CardTitle = forwardRef<HTMLHeadingElement, React.HTMLAttributes<HTMLHeadingElement>>(
  ({ className, children, ...props }, ref) => (
    <h3
      ref={ref}
      className={twMerge(clsx('text-sm font-bold tracking-tight text-white flex items-center gap-2', className))}
      {...props}
    >
      {children}
    </h3>
  )
);
CardTitle.displayName = 'CardTitle';

export const CardDescription = forwardRef<HTMLParagraphElement, React.HTMLAttributes<HTMLParagraphElement>>(
  ({ className, children, ...props }, ref) => (
    <p
      ref={ref}
      className={twMerge(clsx('text-xs text-studio-muted leading-relaxed', className))}
      {...props}
    >
      {children}
    </p>
  )
);
CardDescription.displayName = 'CardDescription';

export const CardContent = forwardRef<HTMLDivElement, React.HTMLAttributes<HTMLDivElement>>(
  ({ className, children, ...props }, ref) => (
    <div ref={ref} className={twMerge(clsx('text-xs text-studio-text', className))} {...props}>
      {children}
    </div>
  )
);
CardContent.displayName = 'CardContent';

export const CardFooter = forwardRef<HTMLDivElement, React.HTMLAttributes<HTMLDivElement>>(
  ({ className, children, ...props }, ref) => (
    <div
      ref={ref}
      className={twMerge(
        clsx('flex items-center justify-between gap-3 pt-3 mt-3 border-t border-studio-border/60 text-xs', className)
      )}
      {...props}
    >
      {children}
    </div>
  )
);
CardFooter.displayName = 'CardFooter';
