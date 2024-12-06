import { css } from 'lit';

export const addUserOrGroupSharedStypes = css`
  .acc-form {
    width: 50%;
  }
  .acc-form__block {
    display: flex;
    align-items: center;
    justify-content: center;
    width: 500px;
  }
  .acc-form__login-types,
  .acc-form__lan-id-types {
    display: flex;
    align-items: center;
    justify-content: center;
    width: 300px;
  }
  .acc-filter__srch-td {
    width: 80%;
  }
  .acc-filter__btn-td {
    width: 50px;
  }
  .acc-filter__ldr-td {
    width: 12px;
  }
  .acc-filter__txf {
    width: 380px;
  }
  .acc-filter__btn {
    top: 16px;
  }
  .acc-filter__small-loader {
    border: 2px solid #f3f3f3; /* Light grey */
    border-top: 2px solid #3498db; /* Blue */
    border-radius: 50%;
    width: 12px;
    height: 12px;
    animation: spin 2s linear infinite;
    vertical-align: bottom;
  }
  .acc-filter__span {
    color: darkred;
  }
  @keyframes spin {
    0% {
      transform: rotate(0deg);
    }
    100% {
      transform: rotate(360deg);
    }
  }
`;
