import React from 'react';
import { ReviewWorkspace, ReviewWorkspaceProps } from '../../views/ReviewWorkspace';

export type { ReviewWorkspaceProps as ReviewPlaceholderViewProps };

export const ReviewPlaceholderView: React.FC<ReviewWorkspaceProps> = (props) => {
  return <ReviewWorkspace {...props} />;
};
