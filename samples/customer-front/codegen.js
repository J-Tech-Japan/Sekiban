/* eslint-disable no-undef */
/* eslint-disable @typescript-eslint/no-var-requires */
process.env["NODE_TLS_REJECT_UNAUTHORIZED"] = 0;
const OpenAPI = require("@j-tech-japan/openapi-typescript-codegen");

OpenAPI.generate({
  input: "http://localhost:5224/swagger/v1/swagger.json",
  output: "./src/openapi",
  useOptions: true,
  useUnionTypes: true,
  exportSchemas: true,
});
