import React, { forwardRef } from 'react';
import { clsx } from 'clsx';
import { twMerge } from 'tailwind-merge';

export type ButtonVariant =
  | 'primary'
  | 'secondary'
  | 'outline'
  | 'ghost'
  | 'danger'
  | 'success'
  | 'turquoise';

export type ButtonSize = 'xs' | 'sm' | 'md' | 'lg';

export interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  size?: ButtonSize;
  leftIcon?: React.ReactNode;
  rightIcon?: React.ReactNode;
  isLoading?: boolean;
}

const variantStyles: Record<ButtonVariant, string> = {
  primary:
    'bg-studio-primary hover:bg-studio-primaryHover active:bg-studio-primaryActive text-white shadow-glow-blue border border-blue-500/30',
  secondary:
    'bg-studio-card hover:bg-studio-cardHover active:bg-studio-cardActive text-studio-text border border-studio-border hover:border-studio-borderLight',
  outline:
    'bg-transparent hover:bg-studio-panel active:bg-studio-card text-studio-text border border-studio-border hover:border-studio-borderLight',
  ghost:
    'bg-transparent hover:bg-studio-card/70 active:bg-studio-card text-studio-muted hover:text-studio-text border border-transparent',
  danger:
    'bg-studio-error/15 hover:bg-studio-error/25 active:bg-studio-error/35 text-red-300 border border-studio-error/30 hover:border-studio-error/50',
  success:
    'bg-studio-success/15 hover:bg-studio-success/25 active:bg-studio-success/35 text-emerald-300 border border-studio-success/30 hover:border-studio-success/50',
  turquoise:
    'bg-studio-turquoise hover:bg-studio-turquoiseHover text-studio-bg font-bold shadow-glow-cyan border border-cyan-400/30'
};

const sizeStyles: Record<ButtonSize, string> = {
  xs: 'h-7 px-2 text-xs gap-1.5 rounded-md font-medium',
  sm: 'h-8 px-2.5 text-xs gap-1.5 rounded-lg font-semibold',
  md: 'h-9 px-3.5 text-xs gap-2 rounded-lg font-semibold',
  lg: 'h-10 px-4 text-sm gap-2.5 rounded-xl font-semibold'
};

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(
  (
    {
      className,
      variant = 'secondary',
      size = 'md',
      leftIcon,
      rightIcon,
      isLoading = false,
      disabled,
      children,
      type = 'button',
      ...props
    },
    ref
  ) => {
    return (
      <button
        ref={ref}
        type={type}
        disabled={disabled || isLoading}
        className={twMerge(
          clsx(
            'inline-flex items-center justify-center select-none transition-all duration-150 relative cursor-pointer',
            'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-studio-borderFocus focus-visible:ring-offset-1 focus-visible:ring-offset-studio-bg',
            'disabled:opacity-40 disabled:cursor-not-allowed disabled:pointer-events-none',
            variantStyles[variant],
            sizeStyles[size],
            className
          )
        )}
        {...props}
      >
        {isLoading ? (
          <span className="w-3.5 h-3.5 border-2 border-current border-t-transparent rounded-full animate-spin shrink-0" />
        ) : leftIcon ? (
          <span className="shrink-0">{leftIcon}</span>
        ) : null}

        <span>{children}</span>

        {!isLoading && rightIcon && <span className="shrink-0">{rightIcon}</span>}
      </button>
    );
  }
);

Button.displayName = 'Button';
