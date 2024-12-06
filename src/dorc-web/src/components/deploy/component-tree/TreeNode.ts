export class TreeNode {
  id!: number;
  name!: string;
  icon!: string;
  open!: boolean;
  children!: TreeNode[];
  numOfChildren!: number;
  hasParent!: boolean;
  parentId!: number | undefined;
  checked!: boolean;
  indeterminate!: boolean;
}
