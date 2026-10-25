import { TractiveForceRequiredInput } from './TractiveForceRequiredInput';
import { TractiveForceSolveFor } from './TractiveForceSolveFor';
import type { TractiveForceFormProps } from './TractiveForceSolveFor';

/** Source control composition. Its hard-coded sample chart is deliberately not presented as a computed result. */
export function TractiveForceControl(props: TractiveForceFormProps) {
  return <div className="stable-form" data-testid="stable-form">
    <TractiveForceSolveFor {...props} />
    <TractiveForceRequiredInput {...props} />
  </div>;
}
