import * as React from "react";
import { Slot } from "@radix-ui/react-slot";
import { cva, type VariantProps } from "class-variance-authority";

import { cn } from "@/lib/utils";

const buttonVariants = cva(
  "button-control inline-flex min-h-11 items-center justify-center gap-2 whitespace-nowrap text-sm font-medium disabled:pointer-events-none disabled:cursor-not-allowed disabled:opacity-50",
  {
    variants: {
      variant: {
        primary: "button-primary",
        outline: "button-outline",
        ghost: "button-ghost",
      },
      size: {
        default: "px-4 py-2",
        sm: "min-h-10 px-3 py-2",
        lg: "min-h-12 px-5 py-3 text-base",
        icon: "size-11",
      },
      state: {
        idle: "",
        loading: "is-loading",
        error: "is-error",
        success: "is-success",
      },
    },
    defaultVariants: {
      variant: "primary",
      size: "default",
      state: "idle",
    },
  },
);

export interface ButtonProps
  extends React.ButtonHTMLAttributes<HTMLButtonElement>,
    VariantProps<typeof buttonVariants> {
  asChild?: boolean;
}

function Button({ className, variant, size, state, asChild = false, ...props }: ButtonProps) {
  const Comp = asChild ? Slot : "button";
  return (
    <Comp
      className={cn(buttonVariants({ variant, size, state, className }))}
      data-state={state ?? "idle"}
      aria-busy={state === "loading" || undefined}
      {...props}
    />
  );
}

export { Button, buttonVariants };

