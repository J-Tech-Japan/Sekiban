import LoginForm from "./LoginForm";

export default function LoginPage() {
  return <LoginForm development={process.env.NODE_ENV === "development"} />;
}
