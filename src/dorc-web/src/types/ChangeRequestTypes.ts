/**
 * Custom types for Change Request validation
 * These types are used until the API swagger.json is updated and regenerated
 */

/**
 * Result of validating a Change Request against ServiceNow
 */
export interface ChangeRequestValidationResult {
  /**
   * Whether the CR is valid and in the correct state for deployment
   */
  IsValid?: boolean;
  
  /**
   * Message describing the validation result
   */
  Message?: string | null;
  
  /**
   * Current state of the CR in ServiceNow (e.g., "Implement", "Scheduled", etc.)
   */
  State?: string | null;
  
  /**
   * The CR number that was validated
   */
  CrNumber?: string | null;

  /**
   * Short description of the change request
   */
  ShortDescription?: string | null;

  /**
   * Start of the approved change window
   */
  StartDate?: string | null;

  /**
   * End of the approved change window
   */
  EndDate?: string | null;
}
